#!/usr/bin/env python3
"""Sample data for UAT (Python 3.9+, standard library only; reuses the helpers of scripts/e2e/main_flow.py).

Creates, once, a demo agency with everything a Partner tester needs: registered, escrow paid (ACTIVE), Pro package (Verified Partner),
guarantee signed, bank account, 3 workers and a roster for the next days. Running it again is harmless (it reuses what exists).
Needs the backend in Development (dev payment tool). Prints the logins at the end.
"""
import argparse
import datetime
import os
import sys

sys.dont_write_bytecode = True  # importing main_flow must not leave a __pycache__ in the repository
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'e2e'))
import main_flow as mf  # noqa: E402

EMAIL = 'demo.agency@example.com'
PASSWORD = 'Demo-Passw0rd!'
WORKERS = [('Demo Tho Mot', '0911000001', '079300000001'), ('Demo Tho Hai', '0911000002', '079300000002'), ('Demo Tho Ba', '0911000003', '079300000003')]
ROSTER_DAYS = 7


def pay_open_payment(reference):
    _, response = mf.call('GET', '/api/dev/payments')
    open_payments = [p for p in mf.data_of(response) if p['gatewayTxnRef'] == reference]
    if not open_payments:
        sys.exit('The payment %s is not in the dev tool: is the backend in Development?' % reference)
    status, _ = mf.call('POST', f"/api/dev/payments/{open_payments[0]['paymentId']}/confirm")
    if status != 200:
        sys.exit('Could not confirm the payment (%s).' % status)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--base-url', default='http://localhost:5004')
    parser.add_argument('--admin-email', default='admin@dev.local')
    parser.add_argument('--admin-password', default=os.environ.get('E2E_ADMIN_PASSWORD'), help='the dev admin password (Seed:Admin:Password); or env E2E_ADMIN_PASSWORD')
    args = parser.parse_args()
    if not args.admin_password:
        parser.error('--admin-password (or E2E_ADMIN_PASSWORD) is required')
    mf.Env.base = args.base_url

    status, _ = mf.call('GET', '/api/dev/payments')
    if status != 200:
        sys.exit('The dev payment tool answers %s: the backend must run in the Development environment.' % status)

    status, response = mf.call('POST', '/api/agencies/registration', {
        'taxCode': '0311111111', 'legalName': 'Cong ty Demo UAT', 'legalRepresentative': 'Nguyen Demo', 'contactPhone': '0911000000',
        'contactEmail': EMAIL, 'password': PASSWORD, 'bankName': 'Vietcombank', 'bankAccountNo': '0011001234567'})
    print('agency:', 'registered' if status == 201 else 'already registered' if status == 409 else f'registration answered {status}: {response}')
    status, response = mf.call('POST', '/api/auth/password/login', {'email': EMAIL, 'password': PASSWORD, 'role': 'Partner'})
    if status != 200:
        sys.exit('The demo agency cannot sign in (%s): %s' % (status, response))
    partner = mf.data_of(response)['accessToken']

    _, response = mf.call('GET', '/api/agencies/escrow', token=partner)
    if mf.data_of(response)['agencyStatus'] != 'ACTIVE':
        _, response = mf.call('POST', '/api/agencies/escrow/deposits', {'amount': 5000000}, partner)
        pay_open_payment(mf.data_of(response)['gatewayTxnRef'])
        print('escrow: 5.000.000 paid, the agency is ACTIVE')
    _, response = mf.call('GET', '/api/agencies/subscriptions/me', token=partner)
    if mf.data_of(response)['tier'] != 'PRO':
        _, response = mf.call('POST', '/api/agencies/subscriptions', {'packageId': 2}, partner)
        pay_open_payment(mf.data_of(response)['gatewayTxnRef'])
        print('subscription: Pro monthly paid')

    body, ctype = mf.multipart('file', 'guarantee.pdf', b'%PDF-1.4\n%%EOF\n', 'application/pdf')
    mf.call('POST', '/api/agencies/guarantee', token=partner, raw=(body, ctype))
    _, template = mf.download('/api/agencies/workers/import/template', partner)
    header = mf.read_xlsx_rows(template)[0]
    body, ctype = mf.multipart('file', 'workers.xlsx', mf.build_xlsx([header] + [[n, p, i, 'VCB', '0011001234'] for n, p, i in WORKERS]), mf.XLSX_TYPE)
    status, response = mf.call('POST', '/api/agencies/workers/import/commit', token=partner, raw=(body, ctype))
    print('workers:', 'imported 3' if status == 200 else 'already imported or rejected (%s)' % status)

    status, response = mf.call('POST', '/api/auth/password/login', {'email': args.admin_email, 'password': args.admin_password, 'role': 'Admin'})
    if status != 200:
        sys.exit('Admin login failed (%s): check --admin-password' % status)
    admin = mf.data_of(response)['accessToken']
    names = {n for n, _, _ in WORKERS}
    worker_ids = []
    for worker_id in range(1, 200):
        status, response = mf.call('GET', f'/api/workers/{worker_id}', token=admin)
        if status == 404:
            break
        if status == 200 and mf.data_of(response)['fullName'] in names:
            worker_ids.append(worker_id)
    if len(worker_ids) != len(WORKERS):
        sys.exit('Found %d of the 3 demo workers (ids %s): reset the database and run again.' % (len(worker_ids), worker_ids))

    today = datetime.date.today()
    entries = [{'workerId': w, 'slotDate': (today + datetime.timedelta(days=d)).isoformat(), 'shiftCode': shift}
               for d in range(1, ROSTER_DAYS + 1) for w in worker_ids for shift in ('SHIFT_MORNING', 'SHIFT_AFTERNOON')]
    status, response = mf.call('PUT', '/api/agencies/roster', {'entries': entries}, partner)
    print('roster:', 'ok (%s new slots)' % mf.data_of(response)['created'] if status == 200 else 'not changed (%s): %s' % (status, str(response)[:200]))

    print('\nLogins (Web %s)' % 'http://localhost:8080')
    print('  Partner portal : %s / %s   (role: Đối tác doanh nghiệp)' % (EMAIL, PASSWORD))
    print('  Admin console  : %s / <Seed:Admin:Password>   (role: Quản trị viên)' % args.admin_email)
    print('  Agency workers : %s  (Mobile, role Chuyên Viên / Thợ, OTP in the backend log)' % ', '.join(p for _, p, _ in WORKERS))


if __name__ == '__main__':
    main()
