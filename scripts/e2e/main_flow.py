#!/usr/bin/env python3
"""End-to-end smoke test of the main business flows over HTTP (Python 3.9+, standard library only).

Runs against a backend in the Development environment (Fake gateway, Fake OTP, dev payment tool):
  premium  an agency registers, is funded, imports workers, rosters them, buys Pro; a customer books a Premium
           shift; the agency worker does the job; the customer accepts; the agency is paid out.
  economy  a freelancer registers (OTP), passes eKYC, opens a slot and shares a location; a customer books an
           Economy shift; the freelancer gets the offer, accepts and does the job; the freelancer is paid out.
See scripts/e2e/README.md. Exit code 0 = every check passed.
"""
import argparse
import datetime
import io
import json
import os
import random
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import zipfile
from xml.etree import ElementTree

NS = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
XLSX_TYPE = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'


# ---------------------------------------------------------------- plumbing
class Env:
    base = 'http://localhost:5004'
    log = ''
    sqlcmd = ''
    server = 'localhost'
    database = 'ChoThueGiupViec'
    sql_user = ''
    sql_password = ''
    admin_email = 'admin@dev.local'
    admin_password = ''
    verbose = False


FAILS = []
RUN = ''.join(random.choice('0123456789') for _ in range(6))


def check(name, ok, detail=''):
    print(('  PASS ' if ok else '  FAIL ') + name + ('' if ok else f'   -> {detail}'))
    if not ok:
        FAILS.append(name)


def call(method, path, body=None, token=None, raw=None):
    headers = {'Accept': 'application/json'}
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        headers['Content-Type'] = 'application/json'
    if raw is not None:
        data, headers['Content-Type'] = raw
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(Env.base + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            status, payload, ctype = resp.status, resp.read(), resp.headers.get('Content-Type', '')
    except urllib.error.HTTPError as e:
        status, payload, ctype = e.code, e.read(), e.headers.get('Content-Type', '')
    if 'json' in ctype:
        try:
            payload = json.loads(payload)
        except ValueError:
            pass
    if Env.verbose:
        print(f'    {method} {path} -> {status}')
    return status, payload


def data_of(response):
    return response['data'] if isinstance(response, dict) else None


def multipart(field, filename, content, content_type):
    boundary = uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="{field}"; filename="{filename}"\r\n'
            f'Content-Type: {content_type}\r\n\r\n').encode() + content + f'\r\n--{boundary}--\r\n'.encode()
    return body, f'multipart/form-data; boundary={boundary}'


def sql(query):
    login = ['-U', Env.sql_user, '-P', Env.sql_password] if Env.sql_user else []
    cmd = [Env.sqlcmd, '-I', '-S', Env.server, '-d', Env.database, *login, '-C', '-W', '-h', '-1', '-Q', 'SET NOCOUNT ON; ' + query]
    out = subprocess.run(cmd, capture_output=True, text=True).stdout
    return [line.strip() for line in out.splitlines() if line.strip()]


def otp_login(phone, role):
    """Requests an OTP (waiting out the 60 s cooldown when needed), reads it from the backend log, verifies it. Returns the verify `data`."""
    for _ in range(40):
        status, _ = call('POST', '/api/auth/otp/request', {'phoneNumber': phone, 'role': role})
        if status == 200:
            break
        time.sleep(3)
    time.sleep(1)
    with open(Env.log, encoding='utf-8', errors='ignore') as f:
        codes = re.findall(r'\[FAKE OTP\] ' + phone + r': (\d+)', f.read())
    if not codes:
        sys.exit(f'No Fake OTP for {phone} in {Env.log}: is the backend console written to that file, and is the environment Development?')
    code = codes[-1]
    _, response = call('POST', '/api/auth/otp/verify', {'phoneNumber': phone, 'role': role, 'code': code})
    return data_of(response)


def dev_confirm(match):
    """Pays an open sandbox payment through the development tool (GET/POST /api/dev/payments)."""
    _, response = call('GET', '/api/dev/payments')
    open_payments = [p for p in data_of(response) if match(p)]
    check('the dev tool lists the open payment', len(open_payments) == 1, str(open_payments)[:200])
    status, response = call('POST', f"/api/dev/payments/{open_payments[0]['paymentId']}/confirm")
    check('the dev tool confirms it', status == 200 and data_of(response)['result'] == 'CONFIRMED', str(response)[:200])


def photos_and_completion(worker_token, customer_token, assignment_id):
    status, response = call('POST', f'/api/workers/assignments/{assignment_id}/photos',
                            {'photoPhase': 'BEFORE', 'angleNo': 1, 'photoUrl': 'https://storage.example/jobs/blur_1.jpg'}, worker_token)
    check('a blurry BEFORE photo is rejected', status == 400, str(response)[:150])
    for phase in ('BEFORE', 'AFTER'):
        for angle in (1, 2, 3):
            status, response = call('POST', f'/api/workers/assignments/{assignment_id}/photos',
                                    {'photoPhase': phase, 'angleNo': angle, 'photoUrl': f'https://storage.example/jobs/{phase.lower()}_{angle}.jpg'}, worker_token)
            check(f'{phase} photo {angle} accepted', status == 200, str(response)[:150])
            if phase == 'BEFORE' and angle == 1:
                state = sql(f'SELECT assignment_status FROM JOB_ASSIGNMENT WHERE assignment_id={assignment_id}')
                check('the first accepted BEFORE photo starts the work (IN_PROGRESS)', state == ['IN_PROGRESS'], str(state))
    status, response = call('POST', f'/api/workers/assignments/{assignment_id}/submit-completion', {'notes': 'done'}, worker_token)
    check('completion submitted', status == 200 and data_of(response)['status'] == 'AWAITING_ACCEPTANCE', str(response)[:200])
    status, response = call('POST', f'/api/customers/assignments/{assignment_id}/accept', {'feedback': 'ok'}, customer_token)
    check('the customer accepts: COMPLETED', status == 200 and data_of(response)['status'] == 'COMPLETED', str(response)[:200])
    return data_of(response)


# ---------------------------------------------------------------- minimal xlsx (no package needed)
def build_xlsx(rows):
    def cell(ref, value):
        text = str(value).replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
        return f'<c r="{ref}" t="inlineStr"><is><t>{text}</t></is></c>'

    sheet_rows = ''.join(
        f'<row r="{r}">' + ''.join(cell(f'{chr(65 + c)}{r}', v) for c, v in enumerate(row)) + '</row>' for r, row in enumerate(rows, start=1))
    parts = {
        '[Content_Types].xml': '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
        '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>',
        '_rels/.rels': '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
        'xl/workbook.xml': '<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="workers" sheetId="1" r:id="rId1"/></sheets></workbook>',
        'xl/_rels/workbook.xml.rels': '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>',
        'xl/worksheets/sheet1.xml': '<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        f'<sheetData>{sheet_rows}</sheetData></worksheet>',
    }
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w', zipfile.ZIP_DEFLATED) as z:
        for name, content in parts.items():
            z.writestr(name, content)
    return buffer.getvalue()


def read_xlsx_rows(content):
    with zipfile.ZipFile(io.BytesIO(content)) as z:
        shared = []
        if 'xl/sharedStrings.xml' in z.namelist():
            shared = [''.join(t.text or '' for t in si.iter('{%s}t' % NS['m'])) for si in ElementTree.fromstring(z.read('xl/sharedStrings.xml')).findall('m:si', NS)]
        sheet = ElementTree.fromstring(z.read('xl/worksheets/sheet1.xml'))
        rows = []
        for row in sheet.iter('{%s}row' % NS['m']):
            values = []
            for c in row.findall('m:c', NS):
                kind = c.get('t')
                if kind == 'inlineStr':
                    values.append(''.join(t.text or '' for t in c.iter('{%s}t' % NS['m'])))
                else:
                    v = c.find('m:v', NS)
                    text = v.text if v is not None else ''
                    values.append(shared[int(text)] if kind == 's' else text)
            rows.append(values)
        return rows


def download(path, token):
    req = urllib.request.Request(Env.base + path, headers={'Authorization': 'Bearer ' + token})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.status, resp.read()


# ---------------------------------------------------------------- scenarios
def free_day():
    """A day within the booking horizon (14 days) on which no worker of the database has a slot yet: the search for a worker looks at every
    agency and freelancer, so a slot left by an earlier run would be picked instead of this run's workers."""
    for ahead in range(2, 14):
        day = (datetime.date.today() + datetime.timedelta(days=ahead)).isoformat()
        if sql(f"SELECT COUNT(*) FROM BOOKING_SLOT WHERE slot_date='{day}' AND shift_code='SHIFT_MORNING'") == ['0']:
            return day
    sys.exit('Every day of the booking horizon already has slots: run against a fresh database (see scripts/e2e/README.md).')


def new_customer(label):
    phone = '098' + RUN + label
    session = otp_login(phone, 'Customer')
    token = session['accessToken']
    _, response = call('POST', '/api/customers/me/addresses', {
        'label': 'Nha', 'addressLine': '12 Nguyen Hue', 'district': 'Quan 1', 'city': 'TP HCM', 'housingType': 'APARTMENT', 'floorAreaM2': 60,
        'numFloors': 1, 'bedrooms': 2, 'bathrooms': 1, 'latitude': 10.7769, 'longitude': 106.7009, 'isDefault': True}, token)
    return token, data_of(response)['addressId']


def book_and_pay(token, address_id, tier, day):
    status, response = call('POST', '/api/booking/orders', {
        'addressId': address_id, 'serviceTier': tier, 'scheduledDate': day, 'shiftCode': 'SHIFT_MORNING', 'customerNote': 'e2e'}, token)
    check(f'{tier} order created (PENDING_PAYMENT)', status == 201 and data_of(response)['orderStatus'] == 'PENDING_PAYMENT', str(response)[:200])
    order_id = data_of(response)['orderId']
    _, response = call('POST', f'/api/payments/orders/{order_id}/qr', token=token)
    reference = data_of(response)['qrPayload'].replace('fake-qr://', '')
    dev_confirm(lambda p: p['gatewayTxnRef'] == reference)
    return order_id


def premium(admin_token_holder):
    print('\n== PREMIUM: agency, Pro package, agency worker ==')
    email, password, tax = f'e2e{RUN}@example.com', 'E2e-Passw0rd!', '03' + RUN + '99'
    status, response = call('POST', '/api/agencies/registration', {
        'taxCode': tax, 'legalName': 'Cong ty E2E', 'legalRepresentative': 'Nguyen Van A', 'contactPhone': '0901' + RUN[:6], 'contactEmail': email,
        'password': password, 'bankName': 'Vietcombank', 'bankAccountNo': '0011 0012 3456'})
    check('agency registered (201)', status == 201, str(response)[:200])
    agency_id = data_of(response)['agencyId']
    status, response = call('POST', '/api/agencies/registration', {
        'taxCode': tax, 'legalName': 'x', 'legalRepresentative': 'y', 'contactPhone': '0901234568', 'contactEmail': 'other' + email, 'password': password})
    check('a duplicate tax code is a 409, not a 500', status == 409 and data_of(response)['code'] == 'AGENCY_TAX_CODE_EXISTS', str(response)[:200])
    _, response = call('POST', '/api/auth/password/login', {'email': email, 'password': password, 'role': 'Partner'})
    partner = data_of(response)['accessToken']
    _, response = call('GET', '/api/agencies/me/bank', token=partner)
    check('the bank account given at registration', data_of(response) == {'bankName': 'Vietcombank', 'bankAccountNo': '001100123456', 'isSet': True}, str(response))
    status, _ = call('PUT', '/api/agencies/me/bank', {'bankName': 'ACB', 'bankAccountNo': '12ab'}, partner)
    check('a malformed account is a 400', status == 400)
    status, response = call('PUT', '/api/agencies/me/bank', {'bankName': 'ACB', 'bankAccountNo': '9988776655'}, partner)
    check('the bank account is updated', status == 200 and data_of(response)['bankName'] == 'ACB', str(response)[:200])

    _, response = call('POST', '/api/agencies/escrow/deposits', {'amount': 5000000}, partner)
    reference = data_of(response)['gatewayTxnRef']
    dev_confirm(lambda p: p['gatewayTxnRef'] == reference)
    _, response = call('GET', '/api/agencies/escrow', token=partner)
    check('escrow 5.000.000 -> the agency is ACTIVE', data_of(response)['agencyStatus'] == 'ACTIVE', str(response)[:200])

    body, ctype = multipart('file', 'guarantee.pdf', b'%PDF-1.4\n%%EOF\n', 'application/pdf')
    status, _ = call('POST', '/api/agencies/guarantee', token=partner, raw=(body, ctype))
    check('guarantee signed', status == 200)
    status, template = download('/api/agencies/workers/import/template', partner)
    check('the import template is an xlsx', status == 200 and template[:2] == b'PK')
    header = read_xlsx_rows(template)[0]
    names = ['Tran Thi B', 'Le Van C', 'Pham Thi D']
    phones = {n: f'0912{RUN[:4]}{i}0' for i, n in enumerate(names)}
    rows = [header] + [[n, phones[n], f'0792{RUN}{i}0', 'VCB', f'00110{RUN}{i}'] for i, n in enumerate(names)]
    body, ctype = multipart('file', 'workers.xlsx', build_xlsx(rows), XLSX_TYPE)
    _, response = call('POST', '/api/agencies/workers/import/dry-run', token=partner, raw=(body, ctype))
    check('import dry-run is valid', data_of(response)['isValid'], str(response)[:300])
    _, response = call('POST', '/api/agencies/workers/import/commit', token=partner, raw=(body, ctype))
    check('3 workers created', data_of(response)['createdWorkers'] == 3, str(response)[:300])
    worker_ids = [int(x) for x in sql(f'SELECT worker_id FROM WORKER WHERE agency_id={agency_id} ORDER BY worker_id')]

    day = free_day()
    status, response = call('PUT', '/api/agencies/roster', {'entries': [{'workerId': w, 'slotDate': day, 'shiftCode': 'SHIFT_MORNING'} for w in worker_ids]}, partner)
    check('roster created for 3 workers', status == 200 and data_of(response)['created'] == 3, str(response)[:200])
    status, _ = call('DELETE', '/api/agencies/roster/99999999', token=partner)
    check('an unknown roster slot is a 404', status == 404)

    _, response = call('POST', '/api/agencies/subscriptions', {'packageId': 2}, partner)
    reference = data_of(response)['gatewayTxnRef']
    dev_confirm(lambda p: p['gatewayTxnRef'] == reference)
    _, response = call('GET', '/api/agencies/subscriptions/me', token=partner)
    check('the Pro package is active', data_of(response)['tier'] == 'PRO', str(response)[:200])
    time.sleep(1)
    check('the Verified Partner label is on', sql(f'SELECT is_verified_partner FROM PARTNER_AGENCY WHERE agency_id={agency_id}') == ['1'])

    customer, address = new_customer('1')
    order_id = book_and_pay(customer, address, 'PREMIUM', day)
    time.sleep(3)
    _, response = call('GET', f'/api/booking/orders/{order_id}/progress', token=customer)
    assignment = data_of(response)['assignments'][0]
    check('the order is assigned to an agency worker', assignment['assignmentStatus'] == 'ASSIGNED', str(response)[:300])
    assignment_id = assignment['assignmentId']
    worker = otp_login(phones[assignment['workerName']], 'Worker')['accessToken']
    status, response = call('POST', f'/api/dispatch/assignments/{assignment_id}/check-in', {'latitude': 10.7769, 'longitude': 106.7009}, worker)
    check('check-in verified by GPS', status == 200 and data_of(response)['isGpsVerified'], str(response)[:200])
    done = photos_and_completion(worker, customer, assignment_id)
    check('Pro commission is 0', done['payoutAmount'] == done['grossAmount'], str(done))
    return {'agency_id': agency_id, 'assignment_id': assignment_id, 'agency_bank': ('ACB', '9988776655'), 'agency_name': 'Cong ty E2E'}


def economy():
    print('\n== ECONOMY: freelancer ==')
    phone = '0933' + RUN
    session = otp_login(phone, 'Worker')
    registration = session.get('registrationToken')
    check('a new phone gets a registration token', bool(registration), str(session)[:200])
    status, response = call('POST', '/api/workers/register', {
        'registrationToken': registration, 'fullName': 'Freelancer E2E', 'nationalId': '0793' + RUN + '00', 'address': 'Quan 1', 'bio': 'e2e'})
    check('the freelancer registered (201)', status == 201, str(response)[:200])
    worker_id = data_of(response)['workerId']
    worker = otp_login(phone, 'Worker')['accessToken']
    status, response = call('POST', '/api/workers/me/ekyc', {'frontCccdUrl': 'https://s/f.jpg', 'backCccdUrl': 'https://s/b.jpg', 'selfieUrl': 'https://s/s.jpg'}, worker)
    check('eKYC auto-approved', status == 200 and data_of(response)['kycStatus'] == 'APPROVED', str(response)[:200])
    day = free_day()
    status, _ = call('POST', '/api/workers/me/slots/toggle', {'slotDate': day, 'shiftCode': 'SHIFT_MORNING', 'isActive': True}, worker)
    check('a slot is opened', status == 200)
    status, _ = call('PUT', '/api/workers/me/location', {'latitude': 999, 'longitude': 106.7}, worker)
    check('a position out of range is a 400', status == 400)
    status, _ = call('PUT', '/api/workers/me/location', {'latitude': 10.7765, 'longitude': 106.7005}, worker)
    check('the location is shared', status == 200)
    status, _ = call('PUT', '/api/workers/me/bank', {'bankName': 'Techcombank', 'bankAccountNo': '1903 4455 6677'}, worker)
    check('the payout account is saved', status == 200)

    customer, address = new_customer('2')
    order_id = book_and_pay(customer, address, 'ECONOMY', day)
    offer = None
    for _ in range(25):
        _, response = call('GET', '/api/dispatch/offers/current', token=worker)
        if data_of(response):
            offer = data_of(response)
            break
        time.sleep(2)
    check('the freelancer is offered the job (30 s)', offer is not None and offer['orderId'] == order_id, str(offer)[:200])
    assignment_id = offer['assignmentId']
    status, response = call('POST', f'/api/dispatch/offers/{assignment_id}/accept', token=worker)
    check('the offer is accepted (ASSIGNED)', status == 200 and data_of(response)['status'] == 'ASSIGNED', str(response)[:200])
    status, response = call('POST', f'/api/dispatch/assignments/{assignment_id}/check-in', {'latitude': 10.7769, 'longitude': 106.7009}, worker)
    check('check-in verified by GPS', status == 200 and data_of(response)['isGpsVerified'], str(response)[:200])
    done = photos_and_completion(worker, customer, assignment_id)
    check('Economy commission 20 % (payout 80 %)', abs(done['payoutAmount'] - done['grossAmount'] * 0.8) < 1, str(done))
    _, response = call('GET', '/api/workers/me/earnings', token=worker)
    check('the earnings show the job', data_of(response)['jobCount'] == 1, str(response)[:200])
    return {'worker_id': worker_id, 'assignment_id': assignment_id, 'worker_token': worker, 'worker_bank': ('Techcombank', '190344556677')}


def payout(admin, results):
    print('\n== PAYOUT: a finished month ==')
    last = datetime.date.today().replace(day=1) - datetime.timedelta(days=1)
    ids = ','.join(str(r['assignment_id']) for r in results.values())
    # A month must be over before it can be paid: date the completions to last month (the only direct database change of this script).
    sql(f"UPDATE JOB_ASSIGNMENT SET completed_at='{last.isoformat()}T05:00:00' WHERE assignment_id IN ({ids})")
    status, response = call('POST', '/api/admin/payout-batches', {'periodMonth': last.strftime('%Y-%m')}, admin)
    check('the payout batch is built', status in (200, 201), str(response)[:200])
    batch_id = data_of(response)['batchId']
    _, response = call('GET', f'/api/admin/payout-batches/{batch_id}', token=admin)
    items = data_of(response)['items']
    if 'premium' in results:
        mine = [i for i in items if i['agencyId'] == results['premium']['agency_id']]
        bank = results['premium']['agency_bank']
        check('the agency item carries its bank account', len(mine) == 1 and (mine[0]['bankName'], mine[0]['bankAccountNo']) == bank, str(mine)[:250])
        _, content = download(f'/api/admin/payout-batches/{batch_id}/export?type=agency-summary', admin)
        check('the agency export carries the account', any(r[1:3] == list(bank) for r in read_xlsx_rows(content)[1:]), str(read_xlsx_rows(content))[:300])
    if 'economy' in results:
        mine = [i for i in items if i['workerId'] == results['economy']['worker_id']]
        bank = results['economy']['worker_bank']
        check('the freelancer item carries its bank account and 80 %', len(mine) == 1 and (mine[0]['bankName'], mine[0]['bankAccountNo']) == bank
              and mine[0]['netAmount'] == 208000, str(mine)[:250])
    status, response = call('POST', f'/api/admin/payout-batches/{batch_id}/confirm', token=admin)
    check('the batch is confirmed (CLOSED)', status == 200 and data_of(response)['batchStatus'] == 'CLOSED', str(response)[:200])
    if 'economy' in results:
        _, response = call('GET', '/api/workers/me/payouts', token=results['economy']['worker_token'])
        check('the freelancer sees the transfer', any(i['itemStatus'] == 'TRANSFERRED' for i in data_of(response)['items']), str(response)[:200])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--base-url', default='http://localhost:5004')
    parser.add_argument('--backend-log', required=True, help='file the backend writes its console to (the Fake OTP is read from it)')
    parser.add_argument('--admin-email', default='admin@dev.local', help='the dev admin email (Seed:Admin:Email)')
    parser.add_argument('--admin-password', default=os.environ.get('E2E_ADMIN_PASSWORD'), help='the dev admin password (Seed:Admin:Password); or env E2E_ADMIN_PASSWORD')
    parser.add_argument('--sqlcmd', default=os.environ.get('SQLCMD', 'sqlcmd'), help='path of sqlcmd (needed to read worker ids and to date the payout month)')
    parser.add_argument('--sql-server', default='localhost')
    parser.add_argument('--database', default='ChoThueGiupViec')
    parser.add_argument('--sql-user', help='SQL login (default: Windows authentication)')
    parser.add_argument('--sql-password', default=os.environ.get('E2E_SQL_PASSWORD'), help='password of --sql-user; or env E2E_SQL_PASSWORD')
    parser.add_argument('--only', choices=['premium', 'economy'])
    parser.add_argument('-v', '--verbose', action='store_true')
    args = parser.parse_args()
    if not args.admin_password:
        parser.error('--admin-password (or E2E_ADMIN_PASSWORD) is required')
    Env.base, Env.log, Env.sqlcmd, Env.server, Env.database = args.base_url, args.backend_log, args.sqlcmd, args.sql_server, args.database
    Env.verbose = args.verbose
    Env.sql_user, Env.sql_password = args.sql_user or '', args.sql_password or ''
    Env.admin_email = args.admin_email

    status, _ = call('GET', '/api/dev/payments')
    if status != 200:
        sys.exit('The dev payment tool answers %s: the backend must run in the Development environment.' % status)
    results = {}
    if args.only != 'economy':
        results['premium'] = premium(None)
    if args.only != 'premium':
        results['economy'] = economy()
    status, response = call('POST', '/api/auth/password/login', {'email': Env.admin_email, 'password': args.admin_password, 'role': 'Admin'})
    if status != 200:
        sys.exit('Admin login failed (%s): check --admin-password' % status)
    payout(data_of(response)['accessToken'], results)

    print('\nRESULT:', 'ALL PASS' if not FAILS else f'{len(FAILS)} FAILED: ' + '; '.join(FAILS))
    sys.exit(1 if FAILS else 0)


if __name__ == '__main__':
    main()
