# Project SDLC profile

Status: incomplete
Policy acceptance: Basic verification profile and delivery policy accepted by user on 2026-09-24; remote enforcement remains unverified
Verification profile: Basic
Artifact root: docs/
Change-directory pattern: feat-{slug}
Slug: proposed by capture-intent
Task source of truth: Git files
Delivery: one integration branch and PR per change, unless agreed otherwise
Delegation: sequential unless supported and authorized

## Standards sources

- `README.md` — project workflow conventions and named project rules (observed; product/team policy source).
- `TurboSquadApp/TurboSquadApp.csproj` — target framework and nullable/implicit-using settings (authoritative build configuration).
- No `AGENTS.md`, architecture decision records, editorconfig, or CI workflow is currently present.

## Verification

| Gate | Requirement and rationale | Command | Scope | Pass condition | Evidence/result | Enforcement |
| --- | --- | --- | --- | --- | --- | --- |
| Lint | Not required: no lint tool or adopted lint policy exists for this minimal .NET project | N/A | N/A | N/A | Justified non-requirement; tooling absent by policy | Unverified |
| Typecheck | Required through the project build; catches compilation and nullable errors | `dotnet build TurboSquadApp.sln --no-restore` | Solution | Exit code 0 with no errors | Pass on 2026-09-24; SDK 10.0.400; local tree | Local only; no CI configured |
| Unit tests | Required when test projects exist; currently no test project is present | `dotnet test TurboSquadApp.sln --no-restore` | Solution | All discovered tests pass | Pass on 2026-09-24; no tests discovered; local tree | Local only; no CI configured |
| E2E | Not applicable for setup: no user-agreed key scenarios or E2E harness exists | N/A | N/A | N/A | Explicitly deferred pending scenario and harness decision | Unverified |
| CRAP | Not required under accepted Basic profile | N/A | N/A | N/A | Basic profile does not require CRAP | Unverified |
| Mutation | Not required under accepted Basic profile | N/A | N/A | N/A | Basic profile does not require mutation testing | Unverified |

Fast feedback commands: `dotnet build TurboSquadApp.sln --no-restore`; `dotnet test TurboSquadApp.sln --no-restore`.
Pre-merge commands: the same commands until CI and a test suite are adopted.
Prerequisites: .NET SDK 10.0.400 or compatible .NET 10 SDK; restore must have completed before `--no-restore` checks.
CRAP method: Not applicable under Basic.
Mutation policy: Not applicable under Basic.
Not-applicable policy: lint, E2E, CRAP, and mutation are explicitly non-required or deferred above; missing tooling is not treated as a passing required check.

## PR gates

Remote repository and target branch: GitHub remote is `origin` (`MolinRE/TurboSquadApp`); target branch observed as `main`; required remote settings not verified.
Required CI check names and agreed enforcement expectations/evidence: None configured; setup gap.
Reviewer and accepted completion signal: One designated reviewer; completion requires the accepted verification profile and no unresolved high-severity findings.
Review-trigger communication authorization: Per user instruction.
Blocking finding/thread policy: Unresolved high-severity findings block completion; lower-severity findings may be tracked for follow-up.
Merge method: Squash merge into `main`.
Merge authorization: Per user instruction, recorded for the specific run.

## Setup gaps

- CI and branch protection are absent or unverified. Proposal: add/verify required GitHub checks after the delivery policy is accepted. Authorization: not yet requested. Next action: user/team decision.
- Reviewer, blocking-finding policy, and merge method are accepted as recorded above. Next action: apply per run.
- No test project or E2E harness exists. Proposal: add them with the first feature that needs those gates. Authorization: not required for setup; implementation requires a separate plan. Next action: defer until feature scope demands it.

Original task to resume, if setup was invoked by another skill: None.
