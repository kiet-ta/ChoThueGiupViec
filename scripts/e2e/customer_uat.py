#!/usr/bin/env python3
"""Customer UAT scenarios of the Mobile app, run against the backend over HTTP (Python 3.9+, standard library only).

Ids follow the manual UAT sheet:
  KH-01 OTP login           KH-02 profile           KH-03 address book       KH-14 favourite workers
  KH-09 Economy, a freelancer accepts                KH-16 two-worker order, one freelancer (BR-02 fallback)
  KH-10 Economy, no freelancer in range              KH-12 another customer sees none of it
Every run uses new phone numbers and gives each dispatch scenario its own random position, so nobody else is offered its orders.
See scripts/e2e/README.md. Exit code 0 = every check passed.
"""
import argparse
import datetime
import json
import random
import re
import subprocess
import sys
import time

sys.dont_write_bytecode = True  # importing main_flow must not leave a __pycache__ folder in the repository
from main_flow import FAILS, RUN, Env, call, check, data_of, dev_confirm  # noqa: E402


class Otp:
    log_file = ''
    compose_file = ''


# ---------------------------------------------------------------- helpers
def backend_log():
    if Otp.compose_file:
        return subprocess.run(['docker', 'compose', '-f', Otp.compose_file, 'logs', 'backend'],
                              capture_output=True, text=True, encoding='utf-8', errors='ignore').stdout
    with open(Otp.log_file, encoding='utf-8', errors='ignore') as f:
        return f.read()


def latest_code(phone):
    codes = re.findall(r'\[FAKE OTP\] (?:\+84|0)?' + phone[1:] + r': (\d+)', backend_log())
    if not codes:
        sys.exit(f'No Fake OTP for {phone} in the backend log: is the environment Development, and is --backend-log / --compose-file right?')
    return codes[-1]


def request_code(phone, role):
    """Requests an OTP, waiting out the 60 s cooldown when needed. Returns the code read from the backend log."""
    for _ in range(40):
        status, response = call('POST', '/api/auth/otp/request', {'phoneNumber': phone, 'role': role})
        if status == 200:
            break
        if status == 429 and 'ip' in str(response).lower():
            sys.exit('The backend refuses more OTP requests from this IP (Otp.MaxPerIpPerHour, 20 per hour; a run needs 7, the emulator shares '
                     'the same IP). Wait up to an hour, or run against a fresh database.')
        time.sleep(3)
    else:
        sys.exit(f'No OTP could be requested for {phone}: {response}')
    time.sleep(1)
    return latest_code(phone)


def verify(phone, role, code):
    return call('POST', '/api/auth/otp/verify', {'phoneNumber': phone, 'role': role, 'code': code})


def login(phone, role):
    _, response = verify(phone, role, request_code(phone, role))
    return data_of(response)


def wrong(code):
    return f'{(int(code) + 1) % 1000000:06d}'


def field_error(status, response, field):
    return status == 400 and field.lower() in json.dumps(response).lower()


def day(ahead):
    return (datetime.date.today() + datetime.timedelta(days=ahead)).isoformat()


BASE = (random.uniform(11.2, 11.6), random.uniform(107.2, 108.4))
SPOTS = []


def spot():
    """A position of its own for each scenario: a random base far from Ho Chi Minh City centre (where the manual UAT data sits), then
    0.3 degree (about 33 km, beyond the 10 km dispatch radius) further north for every scenario of the run."""
    SPOTS.append((round(BASE[0] + 0.3 * len(SPOTS), 6), round(BASE[1], 6)))
    return SPOTS[-1]


def address(token, label, housing, area, floors, at, is_default=False):
    status, response = call('POST', '/api/customers/me/addresses', {
        'label': label, 'addressLine': '12 Le Loi', 'district': 'Quan 1', 'city': 'TP HCM', 'housingType': housing,
        'floorAreaM2': area, 'numFloors': floors, 'latitude': at[0], 'longitude': at[1], 'isDefault': is_default}, token)
    return status, response


def freelancer(prefix, at, slots):
    """Registers a freelancer (OTP, profile, eKYC), opens `slots` [(date, shift)] and shares the position `at`. Returns its token."""
    phone = prefix + RUN
    registration = login(phone, 'Worker').get('registrationToken')
    status, _ = call('POST', '/api/workers/register', {
        'registrationToken': registration, 'fullName': 'Tho UAT ' + prefix, 'nationalId': '0794' + RUN + prefix[-2:], 'address': 'Quan 1'})
    check(f'freelancer {prefix} registered', status == 201)
    token = login(phone, 'Worker')['accessToken']
    status, response = call('POST', '/api/workers/me/ekyc', {'frontCccdUrl': 'https://s/f.jpg', 'backCccdUrl': 'https://s/b.jpg', 'selfieUrl': 'https://s/s.jpg'}, token)
    check(f'freelancer {prefix} eKYC approved', status == 200 and data_of(response)['kycStatus'] == 'APPROVED', str(response)[:200])
    for slot_date, shift in slots:
        status, _ = call('POST', '/api/workers/me/slots/toggle', {'slotDate': slot_date, 'shiftCode': shift, 'isActive': True}, token)
        check(f'freelancer {prefix} opens {slot_date} {shift}', status == 200)
    status, _ = call('PUT', '/api/workers/me/location', {'latitude': at[0], 'longitude': at[1]}, token)
    check(f'freelancer {prefix} shares the position', status == 200)
    return token


def book_and_pay(token, address_id, tier, slot_date, shift):
    status, response = call('POST', '/api/booking/orders', {
        'addressId': address_id, 'serviceTier': tier, 'scheduledDate': slot_date, 'shiftCode': shift, 'customerNote': 'uat'}, token)
    check(f'{tier} order created (PENDING_PAYMENT)', status == 201 and data_of(response)['orderStatus'] == 'PENDING_PAYMENT', str(response)[:200])
    order_id = data_of(response)['orderId']
    _, response = call('POST', f'/api/payments/orders/{order_id}/qr', token=token)
    reference = data_of(response)['qrPayload'].replace('fake-qr://', '')
    dev_confirm(lambda p: p['gatewayTxnRef'] == reference)
    return order_id


def wait_offer(worker, order_id, tries=25):
    for _ in range(tries):
        _, response = call('GET', '/api/dispatch/offers/current', token=worker)
        offer = data_of(response)
        if offer and offer['orderId'] == order_id:
            return offer
        time.sleep(2)
    return None


def wait_for(probe, tries=40, pause=2):
    """Polls `probe` (returns a value or None) until it gives a value."""
    for _ in range(tries):
        value = probe()
        if value is not None:
            return value
        time.sleep(pause)
    return None


def order(token, order_id):
    return data_of(call('GET', f'/api/booking/orders/{order_id}', token=token)[1])


def payment_status(token, order_id):
    _, response = call('GET', f'/api/payments?orderId={order_id}', token=token)
    return [p['txnStatus'] for p in data_of(response) or []]


# ---------------------------------------------------------------- scenarios
def kh01():
    print('\n== KH-01 OTP login ==')
    status, _ = call('POST', '/api/auth/otp/request', {'phoneNumber': '09870002', 'role': 'Customer'})
    check('a phone of 8 digits is refused (400)', status == 400)

    spare = '098' + RUN + '9'
    code = request_code(spare, 'Customer')
    status, _ = call('POST', '/api/auth/otp/request', {'phoneNumber': spare, 'role': 'Customer'})
    check('a resend inside the 60 s cooldown is refused (429)', status == 429)
    statuses = [verify(spare, 'Customer', wrong(code))[0] for _ in range(5)]
    check('wrong codes are 401, the 5th one 429', statuses == [401, 401, 401, 401, 429], str(statuses))
    status, _ = verify(spare, 'Customer', code)
    check('the right code no longer works after 5 wrong ones', status == 401)
    session = login(spare, 'Customer')
    check('a new code logs the customer in', session and session.get('user', {}).get('role') == 'Customer', str(session)[:200])

    phone = '098' + RUN + '1'
    session = login(phone, 'Customer')
    check('customer A logs in (role Customer)', session['user']['role'] == 'Customer', str(session)[:200])
    other = login(spare, 'Worker')
    check('a customer phone with role Worker gets no customer session', not (other or {}).get('accessToken') or other['user']['role'] != 'Customer',
          str(other)[:200])
    return phone, session['accessToken']


def kh02(token, phone):
    print('\n== KH-02 profile ==')
    _, response = call('GET', '/api/customers/me', token=token)
    check('the profile shows the login phone', data_of(response)['phoneNumber'] == phone, str(response)[:200])
    for body, field, name in (({'fullName': '', 'email': None}, 'fullName', 'an empty name'),
                              ({'fullName': 'Tran Thi A', 'email': 'a@'}, 'email', 'a bad email'),
                              ({'fullName': 'x' * 101, 'email': None}, 'fullName', 'a name over 100 characters')):
        status, response = call('PUT', '/api/customers/me', body, token)
        check(f'{name} is refused (400, {field})', field_error(status, response, field), str(response)[:200])
    status, _ = call('PUT', '/api/customers/me', {'fullName': 'Tran Thi A', 'email': 'a@example.com', 'phoneNumber': '0900000000'}, token)
    check('a valid profile is saved', status == 200)
    _, response = call('GET', '/api/customers/me', token=token)
    profile = data_of(response)
    check('the saved name and email come back', (profile['fullName'], profile['email']) == ('Tran Thi A', 'a@example.com'), str(profile))
    check('the phone number did not change', profile['phoneNumber'] == phone, str(profile))


def kh03(token):
    print('\n== KH-03 address book ==')
    at = spot()
    status, response = call('GET', '/api/customers/me/addresses', token=token)
    check('a new customer has no address', status == 200 and data_of(response) == [], str(response)[:200])
    status, response = address(token, 'x' * 51, 'APARTMENT', 25, 1, at)
    check('a label over 50 characters is refused (400, label)', field_error(status, response, 'label'), str(response)[:200])
    status, response = address(token, 'Can ho', 'APARTMENT', 25, 1, at)
    apartment = data_of(response)
    check('apartment 25 m2: total 25, first address is the default', status == 201 and apartment['totalAreaM2'] == 25 and apartment['isDefault'],
          str(response)[:200])
    status, response = address(token, 'Nha pho', 'HOUSE', 40, 3, at)
    house = data_of(response)
    check('house 40 m2 x 3 floors: total 120', status == 201 and house['totalAreaM2'] == 120, str(response)[:200])
    for args, field, name in ((('Tro', 'ROOM', 35, 1), 'floorAreaM2', 'a room over 30 m2'),
                              (('Tro', 'ROOM', 0, 1), 'floorAreaM2', 'an area of 0'),
                              (('Nha', 'HOUSE', 40, 11), 'numFloors', '11 floors')):
        status, response = address(token, *args, at)
        check(f'{name} is refused (400, {field})', field_error(status, response, field), str(response)[:200])
    status, response = address(token, 'Tro', 'ROOM', 20, 1, at)
    room = data_of(response)
    check('a room of 20 m2 is saved', status == 201, str(response)[:200])

    body = {k: house[k] for k in ('label', 'addressLine', 'district', 'city', 'housingType', 'floorAreaM2', 'numFloors', 'latitude', 'longitude')}
    status, _ = call('PUT', f"/api/customers/me/addresses/{house['addressId']}", {**body, 'isDefault': True}, token)
    _, response = call('GET', '/api/customers/me/addresses', token=token)
    defaults = [a['addressId'] for a in data_of(response) if a['isDefault']]
    check('the house becomes the only default', status == 200 and defaults == [house['addressId']], str(defaults))
    body = {k: apartment[k] for k in ('label', 'addressLine', 'district', 'city', 'housingType', 'numFloors', 'latitude', 'longitude')}
    status, response = call('PUT', f"/api/customers/me/addresses/{apartment['addressId']}", {**body, 'floorAreaM2': 28, 'isDefault': False}, token)
    check('the apartment updated to 28 m2 (total 28)', status == 200 and data_of(response)['totalAreaM2'] == 28, str(response)[:200])
    status, _ = call('DELETE', f"/api/customers/me/addresses/{room['addressId']}", token=token)
    gone, _ = call('GET', f"/api/customers/me/addresses/{room['addressId']}", token=token)
    check('the room is deleted (then 404)', status == 200 and gone == 404, f'{status} {gone}')
    return apartment['addressId']


def kh14(token):
    print('\n== KH-14 favourite workers ==')
    status, response = call('GET', '/api/customers/me/favorite-workers', token=token)
    check('the list answers 200, empty for a new customer', status == 200 and data_of(response) == [], str(response)[:200])


def kh09(token, worker, at):
    print('\n== KH-09 Economy, a freelancer accepts ==')
    slot_date = day(3)
    _, response = address(token, 'KH-09', 'APARTMENT', 28, 1, at)
    order_id = book_and_pay(token, data_of(response)['addressId'], 'ECONOMY', slot_date, 'SHIFT_MORNING')
    offer = wait_offer(worker, order_id)
    check('the freelancer is offered the order (30 s)', offer is not None, 'no offer')
    if offer is None:
        return order_id
    status, response = call('POST', f"/api/dispatch/offers/{offer['assignmentId']}/accept", token=worker)
    check('the freelancer accepts', status == 200 and data_of(response)['status'] == 'ASSIGNED', str(response)[:200])
    _, response = call('GET', f'/api/booking/orders/{order_id}/progress', token=token)
    progress = data_of(response)
    check('the order lists the freelancer ASSIGNED', [a['assignmentStatus'] for a in progress['assignments']] == ['ASSIGNED'], str(progress)[:300])
    check('the order is ASSIGNED (the app shows that a worker is found)', progress['orderStatus'] == 'ASSIGNED', progress['orderStatus'])
    return order_id


def kh16(token, worker, at):
    print('\n== KH-16 two-worker order, one freelancer ==')
    for agree in (True, False):
        slot_date = day(4 if agree else 5)
        _, response = address(token, 'KH-16', 'HOUSE', 40, 3, at)
        order_id = book_and_pay(token, data_of(response)['addressId'], 'ECONOMY', slot_date, 'SHIFT_MORNING')
        offer = wait_offer(worker, order_id)
        check('seat 1 is offered to the only freelancer', offer is not None, 'no offer')
        if offer is None:
            continue
        status, _ = call('POST', f"/api/dispatch/offers/{offer['assignmentId']}/accept", token=worker)
        check('the freelancer accepts seat 1', status == 200)

        def waiting():
            state = data_of(call('GET', f'/api/dispatch/orders/{order_id}/fallback', token=token)[1])
            return state if state and state['state'] == 'AWAITING_CUSTOMER' else None

        state = wait_for(waiting)
        check('the customer is asked: 1 worker, 2 consecutive shifts (AWAITING_CUSTOMER)', state is not None, 'still not waiting for the customer')
        if state is None:
            continue
        check('the second shift is the afternoon of the same day', (state['secondShiftCode'], state['secondShiftDate']) == ('SHIFT_AFTERNOON', slot_date),
              str(state))
        status, response = call('POST', f'/api/dispatch/orders/{order_id}/fallback/respond', {'accept': agree}, token)
        if agree:
            check('the customer agrees: the worker is asked (WORKER_ASKED)', status == 200 and data_of(response)['outcome'] == 'WORKER_ASKED',
                  str(response)[:200])
            second = wait_offer(worker, order_id)
            check('the worker gets the second-shift offer', second is not None and second['assignmentId'] != offer['assignmentId'], str(second)[:200])
            if second:
                status, _ = call('POST', f"/api/dispatch/offers/{second['assignmentId']}/accept", token=worker)
                check('the worker accepts the second shift', status == 200)
                check('the order is ASSIGNED', order(token, order_id)['orderStatus'] == 'ASSIGNED', str(order(token, order_id))[:200])
        else:
            check('the customer declines: the order is cancelled', status == 200 and data_of(response)['outcome'] == 'CANCELLED', str(response)[:200])
            check('the order is CANCELLED', order(token, order_id)['orderStatus'] == 'CANCELLED')
            refunded = wait_for(lambda: 'REFUNDED' if 'REFUNDED' in payment_status(token, order_id) else None, tries=10)
            check('the payment is refunded', refunded == 'REFUNDED', str(payment_status(token, order_id)))


def kh10(token):
    print('\n== KH-10 Economy, no freelancer in range ==')
    at = spot()
    _, response = address(token, 'KH-10', 'HOUSE', 60, 1, at)
    order_id = book_and_pay(token, data_of(response)['addressId'], 'ECONOMY', day(6), 'SHIFT_EVENING')
    cancelled = wait_for(lambda: order(token, order_id) if order(token, order_id)['orderStatus'] == 'CANCELLED' else None, tries=15)
    check('the order is CANCELLED', cancelled is not None, order(token, order_id)['orderStatus'])
    if cancelled:
        check('the reason is BR-03 (no worker within 10 km)', '10 km' in (cancelled['cancelReason'] or ''), cancelled['cancelReason'])
    refunded = wait_for(lambda: 'REFUNDED' if 'REFUNDED' in payment_status(token, order_id) else None, tries=10)
    check('the payment is refunded', refunded == 'REFUNDED', str(payment_status(token, order_id)))


def kh12(token_a, order_id, address_id):
    print('\n== KH-12 another customer sees none of it ==')
    other = login('098' + RUN + '2', 'Customer')['accessToken']
    status, response = call('GET', '/api/customers/me/addresses', token=other)
    check('customer C has an empty address book', status == 200 and data_of(response) == [], str(response)[:200])
    status, _ = call('GET', f'/api/booking/orders/{order_id}', token=other)
    check("customer C cannot read customer A's order (404)", status == 404, str(status))
    status, _ = call('GET', f'/api/customers/me/addresses/{address_id}', token=other)
    check("customer C cannot read customer A's address (404)", status == 404, str(status))
    _, response = call('GET', '/api/booking/orders', token=other)
    check('customer C has no order', data_of(response)['total'] == 0, str(response)[:200])
    status, response = call('GET', f'/api/booking/orders/{order_id}', token=token_a)
    check('customer A still reads its order', status == 200, str(response)[:200])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--base-url', default='http://localhost:5004')
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--backend-log', help='file the backend writes its console to (the Fake OTP is read from it)')
    source.add_argument('--compose-file', help='docker-compose.yml of a stack whose service "backend" logs the Fake OTP')
    parser.add_argument('--only', nargs='+', choices=['KH-01', 'KH-02', 'KH-03', 'KH-14', 'KH-09', 'KH-16', 'KH-10', 'KH-12'],
                        help='run only these scenarios (KH-01 always runs: it logs customer A in)')
    parser.add_argument('-v', '--verbose', action='store_true')
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')  # server messages are Vietnamese; a Windows console defaults to cp1252
    Env.base, Env.verbose = args.base_url, args.verbose
    Otp.log_file, Otp.compose_file = args.backend_log or '', args.compose_file or ''
    wanted = set(args.only or ['KH-02', 'KH-03', 'KH-14', 'KH-09', 'KH-16', 'KH-10', 'KH-12'])

    status, _ = call('GET', '/api/dev/payments')
    if status != 200:
        sys.exit('The dev payment tool answers %s: the backend must run in the Development environment.' % status)

    phone, token = kh01()
    if 'KH-02' in wanted:
        kh02(token, phone)
    address_id = kh03(token) if 'KH-03' in wanted or 'KH-12' in wanted else None
    if 'KH-14' in wanted:
        kh14(token)
    order_id = None
    if wanted & {'KH-09', 'KH-16', 'KH-12'}:
        # One freelancer serves every dispatch scenario (accepting a future job leaves it IDLE, decision D6), so a run needs 7 OTPs.
        at = spot()
        worker = freelancer('0977', at, [(day(3), 'SHIFT_MORNING'), (day(4), 'SHIFT_MORNING'), (day(4), 'SHIFT_AFTERNOON'),
                                         (day(5), 'SHIFT_MORNING'), (day(5), 'SHIFT_AFTERNOON')])
        order_id = kh09(token, worker, at)
        if 'KH-16' in wanted:
            kh16(token, worker, at)
    if 'KH-10' in wanted:
        kh10(token)
    if 'KH-12' in wanted:
        kh12(token, order_id, address_id)

    print('\nRESULT:', 'ALL PASS' if not FAILS else f'{len(FAILS)} FAILED: ' + '; '.join(FAILS))
    sys.exit(1 if FAILS else 0)


if __name__ == '__main__':
    main()
