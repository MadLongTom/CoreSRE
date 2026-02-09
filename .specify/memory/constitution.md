<!--
  Sync Impact Report
  ═══════════════════
  Version change: 0.0.0 → 1.0.0 (initial ratification)
  Modified principles: N/A (first version)
  Added sections:
    - Principle I:   Spec-Driven Development (SDD)
    - Principle II:  Test-Driven Development (TDD) — NON-NEGOTIABLE
    - Principle III: Domain-Driven Design (DDD)
    - Principle IV:  Test Immutability — NON-NEGOTIABLE
    - Principle V:   Interface-Before-Implementation
    - Section:       Development Workflow (strict 5-step)
    - Section:       Naming Conventions & AI Collaboration
    - Section:       Governance
  Removed sections: N/A (first version)
  Templates requiring updates:
    - .specify/templates/plan-template.md        ✅ compatible (Constitution Check section exists)
    - .specify/templates/spec-template.md        ✅ compatible (User Scenarios + Requirements sections align)
    - .specify/templates/tasks-template.md       ✅ compatible (test-first task ordering matches TDD flow)
    - .specify/templates/checklist-template.md   ✅ compatible (generic, no principle-specific content)
    - .specify/templates/agent-file-template.md  ✅ compatible (generic, no principle-specific content)
  Follow-up TODOs: none
-->

# CoreSRE Constitution

## Core Principles

### I. Spec-Driven Development (SDD)
Every feature MUST begin with a written specification before any code is written.
- Specifications MUST define: inputs, outputs, boundary conditions, and exception scenarios.
- Specifications are stored in `docs/specs/` using the format `SPEC-{ID}-{feature-name}.md`.
- A specification MUST be detailed enough that a developer (or AI) can derive all test cases from it without ambiguity.
- Specifications are the single source of truth for expected behavior; when tests contradict a spec, the spec wins and a formal change process is required.
- No code review or merge is permitted for a feature that lacks an associated active specification.

**Rationale**: Forcing specification-first eliminates ambiguity before expensive implementation work begins. It aligns all contributors — human and AI — on the same behavioral contract.

### II. Test-Driven Development (TDD) — NON-NEGOTIABLE
All feature code MUST be preceded by tests derived from the specification.
- The strict Red-Green-Refactor cycle is mandatory: tests MUST fail (Red) before implementation, pass after implementation (Green), then code may be refactored.
- Tests MUST cover: normal paths, boundary conditions, and exception scenarios as enumerated in the specification.
- Test naming convention: `{Method}_{Scenario}_{ExpectedResult}` (e.g., `RegisterUser_WithDuplicateEmail_ReturnsConflict`).
- Tests are organized by DDD layer: `CoreSRE.Domain.Tests`, `CoreSRE.Application.Tests`, `CoreSRE.Infrastructure.Tests`, `CoreSRE.Api.Tests`.
- Minimum coverage targets: Domain 95%, Application 90%, Infrastructure 80%, API 80%.

**Rationale**: Writing tests first forces precise thinking about behavior before implementation. The coverage targets ensure business-critical domain logic receives the highest scrutiny.

### III. Domain-Driven Design (DDD)
The domain model is the single source of truth for all business logic.
- Architecture layers: API → Application → Domain ← Infrastructure. Dependencies flow inward only; reversals are forbidden.
- **Domain layer** (`CoreSRE.Domain`): entities, value objects, aggregate roots, domain events, domain services, repository interfaces. MUST have zero external package dependencies.
- **Application layer** (`CoreSRE.Application`): commands, queries (CQRS), handlers, DTOs, validators. MUST NOT contain business rules or reference Infrastructure directly.
- **Infrastructure layer** (`CoreSRE.Infrastructure`): repository implementations, DbContext, external service integrations. MUST NOT contain business logic or define business interfaces.
- **API layer** (`CoreSRE`): route definitions, request/response mapping, middleware, DI registration. MUST NOT contain business logic or access DbContext directly.
- Entities MUST be created via factory methods or constructors that guarantee a valid state.
- Value objects MUST be immutable.
- Aggregate roots are the sole entry point for external access to aggregate internals.
- Cross-aggregate communication MUST use domain events; direct aggregate-to-aggregate references are forbidden.

**Rationale**: Strict layering prevents business logic leakage into infrastructure or presentation concerns, keeping the domain model testable, portable, and comprehensible.

### IV. Test Immutability — NON-NEGOTIABLE
Once a test is committed, its assertions and input data are permanently locked.
- The following actions are unconditionally forbidden on committed tests:
  - Modifying any `Assert` / assertion statement.
  - Modifying input parameters or test data.
  - Deleting a test case.
  - Marking a test as `[Skip]`, `[Ignore]`, or equivalent.
  - Altering a test to accommodate an implementation change.
- The following actions are permitted:
  - Adding new test cases.
  - Refactoring `Setup` / `Teardown` / helper methods when the refactoring does not change assertion semantics.
  - Renaming variables for readability when the rename does not change semantics.
- When a test fails, the implementation MUST be fixed — never the test.
- When business requirements change, a new specification MUST be written (old spec marked `SUPERSEDED`), new tests MUST be written from the new spec, and old tests MUST be marked `[Obsolete]` with a reference to the new spec (not deleted).

**Rationale**: Tests encode the agreed-upon specification contract. Allowing test modification after the fact would undermine the entire TDD and SDD workflow, turning tests into rubber stamps rather than behavioral guarantees.

### V. Interface-Before-Implementation
Every capability MUST have an interface definition before any concrete implementation is written.
- Interfaces MUST reside in `CoreSRE.Domain/Interfaces/` or `CoreSRE.Application/Interfaces/`.
- Interface signatures MUST match the mock/stub signatures used in the tests written during Step 2.
- Implementation classes MUST reside in the layer appropriate to their concern (domain logic in Domain, infrastructure in Infrastructure).
- No concrete class may be consumed directly by an outer layer; all cross-layer communication goes through interfaces resolved via dependency injection.

**Rationale**: Interface-first design ensures loose coupling, makes the codebase testable with mocks, and enforces the DDD dependency direction rule.

## Development Workflow

The following five-step sequential workflow is mandatory for every feature or change. Steps MUST NOT be skipped, reordered, or executed in parallel.

```
Step 1: Spec    → Write specification (docs/specs/SPEC-{ID}-{name}.md)
Step 2: Test    → Write ALL tests from spec (must fail — Red phase) ⟶ TESTS LOCKED
Step 3: Interface → Define interfaces matching test mocks/stubs
Step 4: Implement → Write concrete implementations (only fix here on failure)
Step 5: Verify  → Run full test suite (Green); refactor implementation only
         ✕ Failure → fix Step 4 only. Modifying Steps 1 or 2 is FORBIDDEN.
```

**Spec change process**: When requirements change, create a new versioned spec (`SPEC-{ID}-{name}.v{N}.md`), mark the old spec `SUPERSEDED`, write new tests from the new spec, mark old tests `[Obsolete]` with the new spec reference, then implement against the new tests.

## Naming Conventions & AI Collaboration

### Backend naming
| Artifact | Convention | Example |
|----------|-----------|---------|
| Project | `CoreSRE.{Layer}` | `CoreSRE.Domain` |
| Test project | `CoreSRE.{Layer}.Tests` | `CoreSRE.Domain.Tests` |
| Entity | PascalCase noun | `User`, `Incident` |
| Value object | PascalCase noun | `EmailAddress`, `Password` |
| Interface | `I` + PascalCase | `IUserRepository` |
| Command | `{Verb}{Noun}Command` | `RegisterUserCommand` |
| Query | `Get{Noun}Query` | `GetUserByIdQuery` |
| Handler | `{Command|Query}Handler` | `RegisterUserCommandHandler` |
| Validator | `{Command|Query}Validator` | `RegisterUserCommandValidator` |

### Frontend naming
| Artifact | Convention | Example |
|----------|-----------|---------|
| Component | PascalCase `.tsx` | `UserProfile.tsx` |
| Hook | camelCase, `use` prefix | `useAuth.ts` |
| Utility | camelCase | `formatDate.ts` |
| Type | PascalCase | `User.ts` |
| API call | camelCase, `api` prefix | `apiGetUsers.ts` |

### AI collaboration rules
- AI agents (including GitHub Copilot) MUST comply with every principle in this constitution without exception.
- AI MUST execute the five-step workflow; skipping steps for "efficiency" is forbidden.
- AI MUST NOT modify committed tests, even if the AI believes the test is wrong.
- When AI detects a conflict between a test and a specification, the AI MUST report the conflict and halt — human decision required.
- AI MUST self-verify against this checklist before producing code:
  1. Specification exists and is confirmed?
  2. Tests written and currently failing (Red)?
  3. Interfaces defined matching test stubs?
  4. Implementation in the correct DDD layer?
  5. All tests passing (Green)?
  6. No committed test modified? If yes → revert immediately.

## Governance

- This constitution is the supreme authority for all development practices in CoreSRE. It supersedes any conflicting guidance, convention, or tooling default.
- **Amendment procedure**: amendments require documentation of the proposed change, review by all core contributors, and unanimous approval. Every amendment MUST update the version number below and record the amendment date.
- **Versioning policy**: MAJOR for principle removals/redefinitions, MINOR for new principles or materially expanded guidance, PATCH for clarifications and typo fixes.
- **Compliance review**: every pull request and code review MUST verify compliance with this constitution. Non-compliant code MUST NOT be merged. Violations trigger immediate revert and require the contributor to redo the work following the five-step workflow.
- **Violation consequences**:
  - Modifying committed test assertions → immediate `git revert`, redo via spec change process.
  - Skipping tests to write implementation → code rejected, tests required before re-submission.
  - Business logic in API or Infrastructure layer → immediate refactoring to Domain/Application.
  - AI violating the five-step workflow → generated code reverted, process restarted from Step 1.

**Version**: 1.0.0 | **Ratified**: 2026-02-09 | **Last Amended**: 2026-02-09
