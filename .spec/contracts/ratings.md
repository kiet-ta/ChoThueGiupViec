# Contract: Ratings (module `Ratings`, owner M6)

> Status: **merged by the leader in PR #64** (ticket BE-M6-00, issue #62). The four endpoints of section 2 are implemented by BE-M6-01a (ticket #120). The open questions of section 4 (M1-M6) are still unanswered by the leader; `IWorkerReputation` waits for the answer to **M3**.
> Sources: `.spec/spec.md` BR-09 · `.spec/decisions.md` Q14, G-2, G-3, G-7 · `.spec/plan/00-overview.md` §4 (`IWorkerReputation`), §5 (`RatingSubmitted`, `JobCompleted`) · `Backend/GiupViec_Physical_DB_MVP5.drawio` table `TWO_WAY_RATING` (+ SC-5) · entity `Backend/Domain/Entities/TwoWayRating.cs`.
> Implements tickets: BE-M6-01 (two-way rating), the `IWorkerReputation` port. UI: MOB-M6-01 (customer rates worker), MOB-M6-02 (worker rates customer), design "MOB-M6-02: Thợ đánh giá khách" (Stitch).
> Conventions (envelope, camelCase, UTC, status codes, roles, policies, 404-for-not-owned) are defined in `identity.md` §1 and apply here unchanged. Items marked **Mx** are not decided by the PRD or `decisions.md`; they are listed in section 4 with a recommended default.

## 1. Data shapes

`Rating` (one row of `TWO_WAY_RATING`; the API never returns the other side's rating to the rated party)
```json
{
  "ratingId": 0,
  "assignmentId": 0,
  "raterRole": "CUSTOMER | WORKER",
  "stars": 1,
  "criteria": { "<key>": 1 },
  "comment": "string | null",
  "createdAt": "ISO-8601 UTC"
}
```
(`TWO_WAY_RATING`: `rating_id, assignment_id, worker_id, rater_role, stars, criteria_json, comment, created_at`; `UNIQUE(assignment_id, rater_role)` = SC-5. `worker_id` is copied from the assignment by the server.)

Criteria keys are **fixed** (Q14). The server rejects any other key and any missing key:

| `raterRole` | Direction | `stars` | `criteria` keys (each integer 1-5) |
|---|---|---|---|
| `CUSTOMER` | customer -> worker | 1-5 | `punctuality`, `cleaningQuality`, `attitude` |
| `WORKER` | worker -> customer | 1-5 | `cooperation`, `workingConditions` |

Storage: `criteria_json` stores the keys exactly as in decisions Q14 (`cleaning_quality`, `working_conditions` in snake_case); the API converts to camelCase.

`RatingWindow`
```json
{
  "assignmentId": 0,
  "canRate": true,
  "reason": "OPEN | NOT_COMPLETED | WINDOW_CLOSED | ALREADY_RATED",
  "opensAt": "ISO-8601 UTC | null",
  "closesAt": "ISO-8601 UTC | null",
  "secondsRemaining": 0
}
```
An assignment that is not the caller's is **404**, never a `reason` (no existence leak, `identity.md` §1). `opensAt` = `completed_at`, `closesAt` = `completed_at + Rating.WindowHours` (48, Q14). This feeds the countdown shown on the rating screens ("Còn 47 giờ 12 phút").

## 2. Endpoints

### 2.1 Customer rates the worker (BE-M6-01, MOB-M6-01) — policy `CustomerOnly`

**`GET /api/customers/me/assignments/{assignmentId}/rating-window`** -> 200 `data: RatingWindow`. 404 if the assignment does not exist or `customer_id` is not the caller.

**`POST /api/customers/me/assignments/{assignmentId}/rating`** -> 201 `data: Rating`
```json
{ "stars": 5, "criteria": { "punctuality": 5, "cleaningQuality": 4, "attitude": 5 }, "comment": "string | null" }
```
One rating per worker: a two-worker order has two assignments and therefore two independent ratings (one card per worker).

| Status | Condition |
|---|---|
| 400 | `stars` not 1-5; missing/extra/out-of-range criteria key; `comment` longer than 500 chars (`TWO_WAY_RATING.comment NVARCHAR(500)`, drawio) |
| 404 | assignment missing or not the caller's |
| 409 | assignment is not `COMPLETED`; window closed (`now > completed_at + 48 h`); a `CUSTOMER` rating already exists for this assignment (also enforced by SC-5, double-click safe: the loser of a race gets 409) |

Effects: row inserted; `RatingSubmitted` published; the worker's rating average changes through `IWorkerReputation` (section 3).

### 2.2 Worker rates the customer (BE-M6-01, MOB-M6-02) — policy `WorkerOnly`

**`GET /api/workers/me/assignments/{assignmentId}/rating-window`** -> 200 `data: RatingWindow`. 404 if `worker_id` is not the caller.

**`POST /api/workers/me/assignments/{assignmentId}/rating`** -> 201 `data: Rating`
```json
{ "stars": 4, "criteria": { "cooperation": 5, "workingConditions": 4 }, "comment": "string | null" }
```
Same status table as 2.1 with the `WORKER` criteria. The worker's rating of a customer is **internal only** (Q14): no endpoint returns it to a customer or to the worker's own list, and it never blocks booking.

Both roles: `Agency` workers (`worker_type = AGENCY_STAFF`) can rate and be rated exactly like freelancers; Q14 states ratings do not change an Agency SLA score.

## 3. Port and events

**`IWorkerReputation`** (port, overview §4; implementer M6, consumer M3 Dispatch for `MatchingScore`). The port exists since BASE-03 (`Backend/Application/Interfaces/Ports/IWorkerReputation.cs`) and this contract follows it:
```text
Task<WorkerReputationDto?> GetAsync(int workerId, CancellationToken ct)      // null when the worker is unknown
WorkerReputationDto(int WorkerId, decimal RatingAvg, int CompletedJobs, decimal SuccessRate)
```
- `RatingAvg` = average `stars` of the `CUSTOMER` ratings of the worker, rounded to 2 decimals (round half away from zero, G-2), 0.00-5.00 (Q14); no rating yet: 0.
- `CompletedJobs` and `SuccessRate`: the port only says "share of accepted jobs that ended COMPLETED" (0-1). Which assignments count as "accepted" and as "ended" is **not decided** (question **M3**), so the real implementation is **not part of BE-M6-01a**; the Fake keeps serving Dispatch until the leader answers.

Events:
- **Handles** `JobCompleted` (M4): nothing to store (the window is derived from `completed_at`); the handler only schedules the "please rate" notification through `INotificationService` (content: Mobile text, no new endpoint).
- **Publishes** `RatingSubmitted(RatingId, AssignmentId, RaterRole, Stars, CreatedAtUtc)` (the record of `Backend/Domain/Events/RatingSubmitted.cs`; it has **no worker id**: a consumer that needs it reads the assignment) after each successful insert (consumers: Admin module for Super-Freelancer auto-revoke, see `admin.md` §2.6). A failing consumer is logged and never turns the saved rating into an error for the rater.

## 4. Open questions (not covered by PRD or decisions; recommended default in bold)

| # | Question | Recommended default |
|---|---|---|
| **M1** | `rater_role` is `VARCHAR(10)`: exact stored values. | **`CUSTOMER` and `WORKER`** (matches the API enum above). |
| **M2** | Who updates `WORKER.rating_avg`? Modules may not write another module's table (overview §3.5). | **`IWorkerReputation` is the source for Dispatch; the `WORKER.rating_avg` column is kept in sync by the M4 Workers module handling `RatingSubmitted`** (`WORKER` is M4's). Needed so Q12 (>= 4.80 / revoke < 4.70) and the 4.00 exclusion read one number. |
| **M3** | Meaning of `successRate` (overview §4 says "rating, tỉ lệ ca thành công"; PRD gives no formula). | **`completed / (completed + CANCELLED_BY_WORKER)` over the worker's assignments; 0 when there are none.** Leader may change. |
| **M4** | Q14: a Freelancer with `rating_avg < 4.00` after >= 10 completed jobs is "excluded from matching until an Admin reviews". The schema has no review flag. | **No schema change: Dispatch (M3) reads `ratingAvg`, `ratedJobs` from `IWorkerReputation` and applies the rule; the Admin "review" = Admin sets nothing yet (rule not enforced until the leader defines how an Admin clears it).** Needs leader decision before M3 implements it. |
| **M5** | Q14: "3 consecutive ratings of <= 2 stars from different workers only raise a flag for Admin review" (customer side). No column to store a flag. | **Derived at read time (no schema change): the Admin customer-reputation screen computes the flag from the last 3 `WORKER` ratings of the customer.** Endpoint to be defined in a later contract once the Admin "Khách hàng & Uy tín" screen is ticketed; the current Figma screen (`65:283`) uses a different scale (see M6). |
| **M6** | Design vs decisions: the Figma customer-audit page (`65:283`) shows a "trust score" with 3.0 lock and 3.2 warning thresholds and anonymous behaviour tags (ADR-0018); decisions Q14/Q21 define neither (Q21 C2: `trust_score` is provisional and unused). The customer-rates-worker mock (`S011-08-D`) shows 5 behaviour chips, Q14 fixes 3 criteria. | **Follow decisions Q14 (3 criteria, 2 worker criteria).** UI must be adapted to the contract; the leader decides if Q14 is extended. |
