# GitHub Copilot Instructions for Vamoose

An AI-assisted group trip planner. Front-end: **Angular 21** (PWA). Back-end: **.NET 10 (ASP.NET Core)**.

---

## Table of Contents
1. [Product Overview & Domain](#product-overview--domain)
2. [Technology Stack](#technology-stack)
3. [Solution Structure](#solution-structure)
4. [Path Aliases (Angular)](#path-aliases-angular)
5. [Architecture](#architecture)
6. [Angular Coding Standards](#angular-coding-standards)
7. [.NET Coding Standards](#net-coding-standards)
8. [Best Practices](#best-practices)
9. [Security (Hackathon Baseline)](#security-hackathon-baseline)

---

## Product Overview & Domain

**Core idea: organizer-led, group-informed.** The organizer is the decision-maker. Travelers supply structured input within a deadline, the AI synthesizes it, and the group then participates through lightweight confirmations (Join / Skip) rather than open-ended debate. Travelers do **not** have equal edit power.

### Roles
- **Organizer** — creates the trip, sets constraints, edits and locks the agenda, sees live headcounts.
- **Traveler** — joins by link, answers the poll, views the agenda, RSVPs to items.

### Core flow
1. **Trip setup** — destination, dates (or flexible window), group size, budget, vibe, hard constraints, invite link.
2. **Preference polling (time-gated)** — AI-generated, destination-aware poll; organizer-set deadline with countdown; reminders at halfway and 1 hour before close; auto-close; late responses are recorded but don't change the plan unless the organizer reopens.
3. **Synthesis & co-planning** — AI summarizes consensus, conflicts, and outliers; drafts a day-by-day agenda; organizer edits via chat; AI re-plans and flags downstream impacts; organizer locks the agenda.
4. **Distribution & live RSVPs** — "Up Next" card with Join / Skip (plus "Join, I'll meet you there" and "Maybe"), RSVP cutoff before each item, live headcount and threshold alerts for the organizer.

### Project context & priorities (hackathon)
- This is a **hackathon project judged on the pitch (about 3 minutes)**, not a shippable product. Favor a polished, demoable happy path over completeness, edge cases, or production hardening.
- **Guard against scope creep.** Build one main appealing feature and one genuinely complex piece; everything else is supporting. If a feature can't be explained in a few seconds of the demo, cut it.
- **Main hooks:** (1) group members stay informed with no effort on their part (automatic agenda distribution, "Up Next", nudges); (2) the time-gated poll solves slow or unresponsive group members.
- **Visualization is a priority:** trips should look eye-popping, with a map/timeline view of the agenda and aggregated poll results rather than plain lists.
- **Enrichment ideas (only if time allows):** weather per day/segment, local events during the trip dates, and weather-aware backup suggestions.
- **Mobile-first** design (PWA); desktop is secondary. Design and verify at phone widths first.
- Intended use cases include friend trips and band/tour travel (shows out of town), so keep language and copy generic to "group trips".

### MVP scope
In: create trip + poll + share link; traveler poll with countdown; AI agenda with a short "why" explanation; organizer chat-edit; traveler "Up Next" card with live headcount for the organizer; map/timeline visualization of the agenda.
Out (for now): payments, real bookings, multi-destination trips, advanced hard-constraint solving.

---

## Technology Stack

### Front-end
- **Framework**: Angular 21+ (installable PWA, mobile-first; travelers primarily use phones)
- **Language**: TypeScript (Strict Mode)
- **Styling**: SCSS with BEM naming convention
- **State Management**: Angular Signals (preferred over RxJS Subjects/BehaviorSubjects)

### Back-end
- **Framework**: .NET 10 / ASP.NET Core (Controllers)

---

## Solution Structure

```
Vamoose/
├── src/
│   ├── Vamoose.Api/             # ASP.NET Core host (controllers/endpoints, middleware, DI composition)
│   ├── Vamoose.Application/     # Use cases, services, DTOs, validators, interfaces
│   ├── Vamoose.Domain/          # Entities, value objects, domain rules (no external dependencies)
│   ├── Vamoose.Infrastructure/  # EF Core, repositories, external clients, implementations of interfaces
│   └── Vamoose.Web/             # Angular app
│       └── src/app/
│           ├── components/        # Reusable UI components
│           ├── pages/             # Route-level components
│           ├── services/          # Injectable services
│           ├── interfaces/        # TypeScript interfaces / API models
│           ├── pipes/
│           ├── directives/
│           ├── functions/         # Pure utility functions
│           └── data/              # Static data and constants
└── Directory.Build.props          # Shared build settings (TargetFramework net10.0, Nullable)
```

Dependency direction: `Api → Application → Domain`; `Infrastructure → Application`. Domain never references outer layers.

---

## Path Aliases (Angular)

Use TypeScript path aliases for imports (adjust to match `tsconfig.json`):
- `@components/*` → `src/app/components/*`
- `@services/*` → `src/app/services/*`
- `@interfaces/*` → `src/app/interfaces/*`
- `@pipes/*` → `src/app/pipes/*`
- `@functions/*` → `src/app/functions/*`
- `@data/*` → `src/app/data/*`
- `@env/*` → `src/environments/*`

---

## Architecture

- Components render and delegate; services hold logic; controllers/endpoints only translate HTTP to application calls.
- Depend on abstractions via constructor DI (.NET) or `inject()` (Angular). Put the AI provider behind an interface so it can be swapped or faked.
- Don't introduce abstractions with only one implementation unless needed for the AI provider or layering.

---

## Angular Coding Standards

### Component Guidelines

1. **Use Standalone Components** — no NgModules
2. **Use `inject()` function** — prefer over constructor injection
3. **Prefix private fields** — underscore prefix (e.g. `_myService`)
4. **Use getters for public access** — expose private fields via getters when needed
5. **Prefer Angular Signals over RxJS** — use `signal()`, `computed()`, `effect()`; use `input()`, `output()`, `model()` signal APIs
6. **Use `@if/@else`, `@for`, `@switch`** — never `*ngIf` / `*ngFor`; always provide `track` in `@for`
7. **Use `takeUntil()` pattern** — for RxJS subscriptions that can't be converted to signals, declare `private onDestroy = new Subject<boolean>()` and complete it in `ngOnDestroy()` (or use `takeUntilDestroyed()`)
8. **Use `ChangeDetectionStrategy.OnPush`** for new components
9. **Use optional chaining (`?.`) for API/CMS-sourced content** — never assume optional nested properties exist

### Service Guidelines

1. **Use `providedIn: 'root'`** — singletons by default
2. **Prefer Signals over RxJS** for state; use RxJS for HTTP streams and transformations
3. **API calls live in services only** — components never call `HttpClient` directly
4. **Use HTTP interceptors** for auth headers and error handling

### Styling Guidelines

1. **Use SCSS** with BEM naming
2. **Component-scoped styles** — keep styles in the component
3. **Global styles in `src/css/`** (or `src/styles/`)
4. **Always use theme/design-token variables** — never hardcode colors or theme-specific values

---

## .NET Coding Standards

### General

1. **Target `net10.0`**; enable `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in `Directory.Build.props`
2. **Use modern C#** — primary constructors, records for DTOs/value objects, pattern matching, collection expressions, `required` members, file-scoped namespaces
3. **Constructor injection only** — no service locator, no `static` access to services
4. **Async all the way** — `async`/`await` with `CancellationToken` passed through every I/O call; never `.Result` / `.Wait()`; suffix async methods with `Async`
5. **Naming** — `PascalCase` for types/members, `_camelCase` for private fields, `I` prefix for interfaces

### API Guidelines

1. **Thin endpoints/controllers** — validate, call the application layer, map to a response
2. **Never expose EF entities** — return DTOs/records
3. **Use `ProblemDetails`** (RFC 9457) for error responses via a global exception handler (`IExceptionHandler`)
4. **Validate input** — data annotations; reject invalid models with 400
5. **Return sensible status codes** — 201 for creates, 404 for missing, 409 for conflicts

### Dependency Injection

- Register services in extension methods per layer (`AddApplication()`, `AddInfrastructure()`)
- Lifetimes: `Scoped` for per-request/DbContext users, `Singleton` only for stateless/thread-safe

### Error Handling

- Throw exceptions for expected failures; catch at the boundary (global handler)
- Don't swallow exceptions

---

## Best Practices

- **User-facing UI** — all UI changes must be clean, intuitive, and polished; avoid rough edges and ambiguous states
- **Mobile-first** — write base styles for phone widths and scale up with `min-width` breakpoints; use touch-friendly targets (at least 44px)
- **Visual polish matters** — prefer map/timeline visuals, smooth transitions, and clear empty/loading states in demo-path screens
- **Demo-path first** — keep the main flow reliable; seed/mock data is acceptable for secondary features
- **Prefer Signals over RxJS** in Angular
- **Type Safety** — avoid `any` in TypeScript; avoid `object`/`dynamic` and null-forgiving `!` in C# unless justified
- **Error Handling** — try/catch around async operations in Angular; global exception handling in .NET
- **Loading States** — use signals for local loading state
- **Small, focused changes** — don't refactor unrelated code or add speculative features
- **Comments** — only where the code cannot show intent; one short line

---

## Security (Hackathon Baseline)

### Broken Access Control
1. Enforce authorization **server-side** on every request — never rely on Angular route guards alone
2. Verify trip membership and role (organizer vs. traveler) before returning or mutating data

### Injection
1. EF Core LINQ / parameterized queries only
2. Validate input server-side (type, length, range)
3. Safe JSON parsing — wrap `JSON.parse()` in try/catch

### XSS Prevention
1. Use Angular template binding — `{{ }}` is auto-escaped
2. Avoid `[innerHTML]` with untrusted content; sanitize via `DomSanitizer` only when unavoidable; never `bypassSecurityTrust*` on user input
3. Never use `eval()` or `Function()`

### Misconfiguration
1. Disable detailed errors and developer exception pages outside Development
2. Configure CORS with explicit origins
3. Never commit secrets (API keys, connection strings); use user-secrets or environment variables

### Logging
1. **Never log sensitive data** — tokens, passwords, PII
2. Error responses must not leak stack traces or internal details

### LLM / AI Security
1. **Prompt injection** — treat all traveler/organizer text as untrusted; isolate it from system instructions; constrain output with JSON schemas
2. **Insecure output handling** — validate model output before rendering or persisting; never render it as raw HTML
3. **Excessive agency** — the AI cannot book, spend, invite, or change locked data; all mutations go through normal authorized application services
4. **Sensitive information disclosure** — don't send PII or other travelers' answers to the model unless required; keep anonymity guarantees
5. **Unbounded consumption** — cap tokens and per-trip AI calls
