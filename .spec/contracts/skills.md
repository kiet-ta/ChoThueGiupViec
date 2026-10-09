# Contract: Skills (module `Skills`, owner M5)

> Status: **DRAFT — proposed for leader approval** (ticket BE-M5-00, issue #231). No endpoint in this document is approved for implementation until the leader approves this contract.
> Sources: `.spec/spec.md` §1.3, §2.2, §2.7, §2.8, §5.3; `.spec/decisions.md` G-7, Q18, Q19, Q22; `.spec/plan/00-overview.md` §4–§6; `Backend/GiupViec_Physical_DB_MVP5.drawio` (`SKILL`, `WORKER_SKILL`); entities, EF configurations, and `Backend/Infrastructure/Persistence/Migrations/20261005133811_InitialCreate.cs` (`IX_WORKER_SKILL_skill_id`); `.spec/contracts/identity.md` §1.
> Implements: BE-M5-04 and related Agency/Worker skill selection and lookup, after contract approval and required gates.

## 1. Conventions and proposal status

- Shared conventions inherit from `identity.md` §1: base path `/api`; JSON UTF-8; camelCase; UTC ISO-8601 timestamps; `ApiResponse<T>`; Bearer access token; roles `Worker`, `Partner`, `Admin`; policies `WorkerOnly`, `PartnerOnly`, `AdminOnly`; common status meanings and validation envelope. A resource outside the caller's ownership returns 404.
- The method/path and candidate DTO property names below are proposals for leader review only. The PRD does not define Skills endpoint paths or exact schema property names. Resolve the questions in §6 before implementation; do not treat candidate names as approved schema.
- `Skill` is a normalized catalog managed by Admin. `WorkerSkill` links workers to catalog entries; Worker and Agency select from existing catalog entries, not free text (PRD §2.7).

## 2. Candidate endpoint map

| Capability | Candidate endpoint | Access | Source-backed behavior |
|---|---|---|---|
| Read catalog | `GET /api/skills` | `WorkerOnly` / `PartnerOnly` candidate | Return selectable catalog entries. Filtering, ordering and pagination are not specified. |
| Create catalog entry | `POST /api/admin/skills` | `AdminOnly` candidate | Admin manages the normalized catalog (PRD §2.7). Exact fields/validation are open. |
| Update catalog entry | `PATCH /api/admin/skills/{skillId}` | `AdminOnly` candidate | Admin manages the catalog; whether entries may be renamed, deactivated, or deleted is open. |
| Read worker's skills | `GET /api/workers/{workerId}/skills` | Owner `WorkerOnly`; `PartnerOnly` for an Agency-owned worker is a candidate | Read `WorkerSkill` assignments. Enforce ownership; exact actor capabilities need approval. |
| Replace worker's skill selections | `PUT /api/workers/{workerId}/skills` | Owner `WorkerOnly`; `PartnerOnly` for an Agency-owned worker is a candidate | Select catalog entries by ID and record years of experience and review status conceptually (PRD §2.7, M5 plan). Whether workers, Agencies, or both can write is unresolved. |

These route names, methods, and access rules are not source-backed API facts; the leader must approve or replace them. There is no free-text `skill_tags` endpoint or `LIKE`-based matching.

### Candidate write DTOs and response statuses

Examples below map only to existing `SKILL` and `WORKER_SKILL` columns. Routes, writable fields, and status codes remain proposals for approval.

`POST /api/admin/skills` candidate request:
```json
{
  "skillCode": "TBD",
  "skillName": "TBD",
  "category": "TBD",
  "description": null
}
```
Candidate response: **201** with the created catalog item. Known constraints are the existing field types/lengths and unique `skill_code`; exact format, category values, and duplicate-code response are open.

`PATCH /api/admin/skills/{skillId}` candidate request contains only fields being changed from `skillCode`, `skillName`, `category`, and `description`. Candidate response: **200** with the catalog item. Whether code changes or `isActive` changes are allowed is open.

`PUT /api/workers/{workerId}/skills` candidate request:
```json
{
  "skills": [
    { "skillId": 0, "yearsOfExperience": null }
  ]
}
```
Candidate response: **200** with the worker's skill assignments, including `isVerified` as a read-only field unless the leader assigns a review role. The replacement semantics (including whether an empty list clears assignments) are open.

Candidate validation/error behavior: malformed fields or values outside approved validation rules return **400** using `identity.md` O5; unauthenticated/forbidden callers use 401/403; nonexistent or not-owned worker/catalog resources use 404; unique-code conflicts may use 409. Handling of unknown/inactive IDs, duplicate IDs, empty selections, experience range, and exact error codes remains open for the leader.

## 3. Existing schema and candidate DTOs

The existing physical schema and EF model define these storage fields. They are not automatically the public JSON contract; endpoint DTO property casing and inclusion remain for leader approval.

| Table | Storage fields / constraints |
|---|---|
| `SKILL` | `skill_id INT IDENTITY PK`; `skill_code VARCHAR(30) UNIQUE`; `skill_name NVARCHAR(100)`; `category VARCHAR(30)`; `description NVARCHAR(255) NULL`; `is_active BIT`; `created_at DATETIME2` |
| `WORKER_SKILL` | `worker_id INT PK/FK`; `skill_id INT PK/FK`; composite primary key `(worker_id, skill_id)`; `years_of_experience DECIMAL(3,1) NULL`; `is_verified BIT`; `created_at DATETIME2` |

Candidate wire fields mapped from those existing names (unapproved):

| Storage field | Candidate JSON property | Notes |
|---|---|---|
| `skill_id` | `skillId` | Catalog identifier; use ID for selection and matching. |
| `skill_code` | `skillCode` | Unique in storage; allowed format and public exposure are open. |
| `skill_name` | `skillName` | Standardized display label. |
| `category` | `category` | Stored as `VARCHAR(30)`; allowed categories are open. |
| `description` | `description` | Nullable. |
| `is_active` | `isActive` | Whether selectable/listed is an open behavior question. |
| `created_at` | `createdAt` | UTC timestamp per shared convention. |
| `worker_id` | `workerId` | Worker identity. |
| `years_of_experience` | `yearsOfExperience` | Nullable decimal with one storage decimal place; validation range is open. |
| `is_verified` | `isVerified` | Stored Boolean; its workflow/authority and matching effect are open. |

Illustrative candidate response (unapproved; included fields may change):

```json
{
  "skillId": 0,
  "skillCode": "TBD",
  "skillName": "TBD",
  "category": "TBD",
  "description": null,
  "isActive": true,
  "createdAt": "2026-10-08T12:00:00Z"
}
```

```json
{
  "workerId": 0,
  "skills": [
    {
      "skillId": 0,
      "yearsOfExperience": null,
      "isVerified": false
    }
  ]
}
```

The wire examples do not imply that all storage fields must be exposed. In particular, `isVerified` is the existing Boolean column; the M5 plan's “verification status” does not define its states, review process, or public representation.

## 4. Validation and matching rules

- Skill selections must reference catalog entries by ID; free-text skill tags are not accepted (PRD §2.7).
- Matching uses the normalized `Skill` / `WorkerSkill` relationship and an ID-indexed query. Do not scan skill text or use `LIKE` for matching (PRD §2.7; M5 plan BE-M5-04).
- A worker's skill record includes the existing nullable `years_of_experience DECIMAL(3,1)` and Boolean `is_verified` fields. Range validation, who may change verification, transitions, and whether verification affects matching are unresolved.
- The Agency import contract must use the same catalog-by-ID rule and must not introduce free-text skill tags (Q18, Q19, PRD §2.7).
- Validation for unknown/inactive IDs, duplicate assignments, empty skill sets, and cross-Agency updates is not defined; see §6.

## 5. Error behavior

Use the shared envelope and status meanings in `identity.md` §1. Candidate behavior, pending leader approval:

| Condition | Candidate response |
|---|---|
| Missing/invalid selection or experience value | 400 with the shared validation envelope; exact fields/rules are open |
| Caller lacks required role | 401 / 403 per identity contract |
| Worker/skill record does not exist or is not owned by the caller | 404, avoiding an existence/ownership leak |
| Duplicate or incompatible assignment/current state | 409 candidate; actual conflict rules are open |
| Unexpected error | 500 without internal details |

No error field names, messages, catalog-active behavior, or endpoint-specific codes are settled by this document until approval.

## 6. Leader questions (must resolve before implementation)

1. Approve or replace the candidate routes, methods, and access policies in §2. Can an Agency select/edit skills for its Agency Staff, can a Worker manage their own skills, or both?
2. Which existing `SKILL` fields should be exposed in the catalog DTO? What are the allowed `category` values and `skill_code` format? Can entries be deactivated, renamed, or deleted once assigned?
3. Which existing `WORKER_SKILL` fields should be exposed, and what are the approved JSON names? The storage key is already `(worker_id, skill_id)`.
4. What validation range applies to nullable `years_of_experience DECIMAL(3,1)`? Which error status/body applies?
5. Who may change `is_verified`, what review workflow does it represent, and does verification affect capacity matching?
6. How should unknown, inactive, duplicate, or non-selectable Skill IDs be handled, and what status/error DTO applies?
7. Define catalog listing search/filter/order/pagination only if needed. Matching remains ID-indexed and must not use free-text `LIKE` scans.
