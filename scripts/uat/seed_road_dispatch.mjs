#!/usr/bin/env node
// UAT sample data for DISPATCH-ROAD-01 (#807): 3 freelancers around Nguyen Hue (Quan 1, HCMC) + 1 customer with an address there.
// When the customer books an ECONOMY order and it is paid, the backend offers it to the freelancer with the shortest ROAD distance first
// (Valhalla), then the next nearest every 30 s. Proof: docker compose logs backend | findstr DISPATCH-ROAD
// Needs Node 18+ and the Development backend (the Fake OTP is read from `docker compose logs backend`). Running it again is harmless.
//   node scripts/uat/seed_road_dispatch.mjs [--base-url http://localhost:5004] [--valhalla-url http://localhost:8002]
import { execFileSync } from 'node:child_process';

const arg = (name, dflt) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : dflt; };
const BASE = arg('--base-url', 'http://localhost:5004');
const VALHALLA = arg('--valhalla-url', 'http://localhost:8002');
const ORDER_POINT = [10.7769, 106.7009]; // Nguyen Hue, Quan 1: default point of the address picker
const CUSTOMER_PHONE = '0988000111';
// Straight line from the order point: A ~2.6 km, B ~3.5 km, C ~4.4 km, but A is across the Saigon river (Thu Thiem). The table printed at
// the end shows the real road numbers.
const WORKERS = [
  ['0922100001', 'Tho Thu Thiem (qua song)', '079500000001', [10.7870, 106.7240]],
  ['0922100002', 'Tho Phu Nhuan', '079500000002', [10.7990, 106.6800]],
  ['0922100003', 'Tho Quan 10', '079500000003', [10.7700, 106.6600]],
];

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(method, path, body, token) {
  const headers = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  let payload = null;
  try { payload = await res.json(); } catch { /* empty body */ }
  return [res.status, payload];
}
const dataOf = (p) => (p && 'data' in p ? p.data : p);

function backendLog() {
  return execFileSync('docker', ['compose', 'logs', 'backend'], { encoding: 'utf8', maxBuffer: 1 << 28 });
}

async function login(phone, role) {
  let status, resp;
  for (let i = 0; i < 40; i++) {
    [status, resp] = await call('POST', '/api/v1/auth/otp/request', { phoneNumber: phone, role });
    if (status === 200) break;
    if (status === 429 && JSON.stringify(resp).toLowerCase().includes('ip')) throw new Error('OTP limit per IP (20/hour) reached: wait or use a fresh database');
    await sleep(3000);
  }
  if (status !== 200) throw new Error(`OTP request for ${phone} answered ${status}: ${JSON.stringify(resp)}`);
  await sleep(1500);
  const codes = [...backendLog().matchAll(new RegExp(`\\[FAKE OTP\\] (?:\\+84|0)?${phone.slice(1)}: (\\d+)`, 'g'))];
  if (!codes.length) throw new Error(`No Fake OTP for ${phone} in the backend log`);
  const [vs, vr] = await call('POST', '/api/v1/auth/otp/verify', { phoneNumber: phone, role, code: codes.at(-1)[1] });
  if (vs !== 200) throw new Error(`OTP verify for ${phone} answered ${vs}: ${JSON.stringify(vr)}`);
  return dataOf(vr);
}

async function seedWorker([phone, name, nationalId, point]) {
  let session = await login(phone, 'Worker');
  if (session.registrationToken) {
    const [s, r] = await call('POST', '/api/v1/workers', { registrationToken: session.registrationToken, fullName: name, nationalId, address: 'Quan 1' });
    if (s !== 201) throw new Error(`register ${phone}: ${s} ${JSON.stringify(r)}`);
    session = await login(phone, 'Worker');
  }
  const token = session.accessToken;
  await call('POST', '/api/v1/workers/me/ekyc', { frontCccdUrl: 'https://s/f.jpg', backCccdUrl: 'https://s/b.jpg', selfieUrl: 'https://s/s.jpg' }, token);
  const [ws, wr] = await call('PUT', '/api/v1/workers/me/work-area', { latitude: point[0], longitude: point[1], radiusKm: 15, districts: ['Quan 1'] }, token);
  if (ws !== 200) throw new Error(`work area ${phone}: ${ws} ${JSON.stringify(wr)}`);
  await call('PUT', '/api/v1/workers/me/location', { latitude: point[0], longitude: point[1] }, token);
  for (let ahead = 1; ahead <= 7; ahead++) {
    const d = new Date(Date.now() + ahead * 86400000);
    const day = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    for (const shift of ['SHIFT_MORNING', 'SHIFT_AFTERNOON']) await call('PUT', `/api/v1/workers/me/slots/${day}/${shift}`, { enabled: true }, token);
  }
  console.log(`worker ${phone}  ${name.padEnd(26)} work point ${point}  slots: next 7 days, morning + afternoon`);
}

async function seedCustomer() {
  const token = (await login(CUSTOMER_PHONE, 'Customer')).accessToken;
  const [, r] = await call('GET', '/api/v1/customers/me/addresses', undefined, token);
  if (!(dataOf(r) || []).length) {
    await call('POST', '/api/v1/customers/me/addresses', {
      label: 'Nha Nguyen Hue', addressLine: '12 Nguyen Hue', district: 'Quan 1', city: 'TP HCM', housingType: 'APARTMENT', floorAreaM2: 60,
      numFloors: 1, bedrooms: 2, bathrooms: 1, latitude: ORDER_POINT[0], longitude: ORDER_POINT[1], isDefault: true }, token);
  }
  console.log(`customer ${CUSTOMER_PHONE} with the address "12 Nguyen Hue" at ${ORDER_POINT}`);
}

function haversineKm([la1, ln1], [la2, ln2]) {
  const rad = (x) => (x * Math.PI) / 180;
  const h = Math.sin(rad(la2 - la1) / 2) ** 2 + Math.cos(rad(la1)) * Math.cos(rad(la2)) * Math.sin(rad(ln2 - ln1) / 2) ** 2;
  return 2 * 6371 * Math.asin(Math.sqrt(h));
}

async function roadTable() {
  let cells;
  try {
    const res = await fetch(`${VALHALLA}/sources_to_targets`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sources: [{ lat: ORDER_POINT[0], lon: ORDER_POINT[1] }], costing: 'motor_scooter', targets: WORKERS.map((w) => ({ lat: w[3][0], lon: w[3][1] })) }) });
    cells = (await res.json()).sources_to_targets[0];
  } catch (e) {
    console.log(`\nValhalla not reachable at ${VALHALLA} (${e.message}): no road table. Without Valhalla the backend offers by straight line.`);
    return;
  }
  const rows = WORKERS.map((w, i) => ({ phone: w[0], name: w[1], road: cells[i]?.distance ?? null, straight: haversineKm(ORDER_POINT, w[3]) }));
  const byRoad = [...rows].sort((a, b) => (a.road ?? 1e9) - (b.road ?? 1e9));
  console.log('\nExpected offer order (road km from Valhalla; straight km for comparison):');
  for (const r of byRoad) console.log(`  ${r.phone}  ${r.name.padEnd(26)} road ${r.road == null ? 'n/a' : r.road.toFixed(2)} km   straight ${r.straight.toFixed(2)} km`);
  const same = byRoad.map((r) => r.phone).join() === [...rows].sort((a, b) => a.straight - b.straight).map((r) => r.phone).join();
  console.log(`  road order == straight order? ${same ? 'YES: this data does not show a difference, move a worker across the river' : 'NO: the road rule changes who is offered first'}`);
}

const [pay] = await call('GET', '/api/v1/dev/payments');
if (pay !== 200) { console.error(`The dev payment tool answers ${pay}: the backend must run in the Development environment.`); process.exit(1); }
for (const w of WORKERS) await seedWorker(w);
await seedCustomer();
await roadTable();
