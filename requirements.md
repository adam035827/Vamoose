# Vamoose Requirements

_Living document. Updated as requirements are discussed. Items marked **TBD** are undecided._

Find the overlap. Go.

“Every group trip has a planner, and a group chat that goes nowhere. Vamoose gives the planner control and the group a voice. We ask everyone what they want, find where their preferences overlap, and build the agenda around it. Then we keep everyone in the loop, one ‘up next’ at a time.”

## 1. Overview

Vamoose is a group trip planning app. A user creates a trip and plans it by chatting with the Vamoose AI agent, which searches the internet and offers options. The group can be looped in with poll style questions, which they have 24 hr to respond too be considered. Once the trip is finalized, the creator sends it to the other travelers.

Agent interactions take the place of a traditional html form. 

_Part 1 covers trip creation, planning and sharing (section 3). Part 2 covers group agreement, exiting, cancelling and locking (section 4)._

## 2. Users

- **Primary user:** any user who creates a trip. That user owns the trip and is the only one who plans it and sends it out.
- **Other users (travelers):** people included in a trip. They receive the finalized trip. What they can do with it is **TBD**.
- A user can be a primary user on one trip and a traveler on another.

## 3. Features (Part 1)

### 3.1 Start a trip
 - the user lands onthe home page with a single prompt - 'Where do you want to go?"
 - the user inputs their basic details. and hit 'Go'
 - in agent comes up with a very short responce about the desination, and always starts with the same first questions:
    - Who are you? [email address]
    - Who all is going? [multiple email addresses]
 - User clicks go and the 'trip' is created and the details are saved along with all the users and their roles. 
 - The agent reponds with questions about the group and gets any initial details from the 'planner'

> **Agent acceptance (3.1):** actions `Chat` (destination blurb + first questions) and `Draft` (initial plan from the details gathered). `Draft` is implemented (see 5.1). Both must meet 6.1. On the demo path.

### 3.2 Login

- There is no login - each user gets a magic link emailed to them which is the only access needed for a trip
- No password and no email verification in this version.

### 3.2 Create a trip

- The user must create a "Trip" before using the agent.
- A user can have multiple trips - each with it's own magic link. The user picks one to open its chat.


### 3.3 Home page and Vamoose agent

- The home page is just a chat interface with the Vamoose agent.
- The user uses the chat to plan and fine-tune the trip details.
- The agent searches the internet for information and presents options to the user.
- The user chooses from the options, and the choices are saved to the trip.
- The agent only suggests: it shows options with details, prices and links from web search. It does not book anything or take payments. The user books outside the app and the chosen option is recorded in the trip.
- Chat history is saved per trip. The user can return later and continue the conversation, and the agent keeps the context of earlier messages.

> **Agent acceptance (3.3):** action `Chat` with per-trip history and a web search tool. Every option it presents includes a price, a source link and the time it was fetched (see 3.4). Must meet 6.1. On the demo path.

### 3.4 Trip details

A trip contains:

- Flight details
- Hotel details
- Restaurant details
- Other users included in the trip
- Planned activities

Exact fields for each of these are **TBD**. Minimum fields, matching what the agent already produces (`DraftItem` in 5.1):

| Field | Notes |
| --- | --- |
| `day` | 1-based trip day |
| `date` | Only when exact dates are known |
| `startTime`, `endTime` | `HH:mm`, local to the destination |
| `category` | `Flight`, `Lodging`, `Activity`, `Dining`, `Transit`, `Free` |
| `title`, `description` | Short; description is one sentence |
| `locationName` | Free text |
| `estimatedCostPerPerson` | `{ amount, currency }` |
| `energyLevel` | `Low`, `Medium`, `High` |

Required additions (implemented in the agent's plan contract, `PlanSlot` and `PlanOption`):

- `alternatives`: each item keeps the other options considered during planning, so 4.2 can offer them without inventing new ones.
- `sourceUrl` and `priceFetchedAt` on the chosen option and each alternative, so prices and links from web search are traceable.

### 3.5 Finalize and send trip

- When the trip details are final, the primary user sends the trip to all other users in the trip.
- Other travelers are added to a trip by entering their email addresses.
- The finalized trip is delivered in-app only. A traveler sees it after opening their magic link (3.2). No email notification is sent; the magic link email is the only email.
- What travelers can do with the received trip (view, accept or decline, comment) will be decided later.
- After sending, the primary user can still edit the trip. Edits update what travelers see in-app, and travelers are notified when the primary user edits a sent trip.
- Finalizing and sending are the same action: when the primary user finalizes the trip, it is sent to all other users. Finalizing is a separate step from locking (see 4.6).
- After finalizing, the primary user can still edit the trip during the Agreement phase (see 4.3).
- A trip can be sent again, for example after being unlocked.

## 4. Features (Part 2): group agreement

Once a trip is sent, the Vamoose agent helps the group agree on one plan.

### 4.1 Preference questions

- The agent uses the trip details to generate a set of questions, which each user sees in the app.
- Each user, including the primary user, enters their choices.
- Question types (single choice, multiple choice, free text, rating) and how many questions are generated are **TBD**.

> **Agent acceptance (4.1):** action `Questions`. Output is checked against a fixed structure (question type, prompt, options). Must meet 6.1.

### 4.2 Plan evaluation

- After the users answer, the agent gathers everyone's responses and decides whether the plan works for all users.
- The plan works for all users only when **everyone approves** it (unanimous). Any dissent triggers alternatives and nudging.
- If a user does not like something in the plan, they see alternatives.
- Alternatives come from the original trip plan, so the agent must keep alternatives for each item (flights, hotels, restaurants, activities) while planning in Part 1. The agent does not invent new alternatives at this stage.

> **Agent acceptance (4.2):** action `Analyze`. Reads stored answers and the plan; returns, per item, who approves, who dissents, and which stored alternatives apply. Must meet 6.1.

### 4.3 Change requests and approvals

1. A user selects an alternative for an item in the plan.
2. The primary user is notified and can **approve** or **deny** the change.
3. If denied, the requesting user is notified that their change was denied. The plan is unchanged.
4. If approved, the plan is updated and all users are notified of the change.
5. All other users can then **approve** or **deny** the changed plan.

- The primary user has the final word on the plan.
- Notifications are in-app (consistent with Part 1: no email). They are delivered live while the user is on the page (real-time updates) and also kept in a notification list with an unread badge, so users see what they missed while offline.
- If the primary user denies a change that most other users want, the primary user's decision stands. The other users are informed of the outcome.
- Other users' approvals and denials of an approved change are recorded and shown to the primary user and the agent. Because the plan only counts as agreed when everyone approves, a denial triggers the alternatives and nudging flow again. The primary user can still finalize or lock the trip over a denial; a user who disagrees can then leave the trip (4.5, 4.6).
- Changes are approved one item at a time (for example one flight, one hotel). They are not batched.
- The primary user can edit the trip directly after finalizing but before everyone has approved. Those edits update what travelers see, and are shown to the other users as changes they can approve or deny. Such an edit clears the earlier approvals, so users approve again.

### 4.4 Consensus nudging

- The agent tracks how many users back each option. Example: with 10 users, if 8 approve the plan and 2 choose an alternative, the 2 get a message that most of the other users chose the other option.
- The agent then tries to persuade those users to switch, so the whole group ends up on the same plan.
- Nudging is conversational: the agent talks with that user in the chat, answers their questions, and makes the case for the majority option over several messages.
- The agent persuades with information: how many others chose the option, and the trade-offs of each choice. It does not pressure, mislead or hide the user's right to keep their choice. The user's selection stays their own decision.
- The threshold is a **majority**: users whose choice is held by fewer than half of the group are the ones nudged. Example: if 4 of 10 users select an alternative, those 4 are nudged toward the majority option.
- If a user will not switch, the plan does not wait for them. The primary user can finalize or lock over their denial, and the user can leave the trip (4.5).
- The agent nudges a user 2 times, both in the same chat session. After that it stops. The count resets when the plan changes.
- In an exact tie (for example 5 of 10), the primary user breaks the tie.
- The primary user's decision is a separate vote on top of the group's votes, acting like a veto. It is not counted as one of the group's votes.

> **Agent acceptance (4.4, 4.5):** action `Nudge` (also used for "persuade to stay" in 4.5). The 2-nudge limit and its reset are enforced by `Vamoose.Api`, not by the agent prompt. Must meet 6.1.

### 4.5 Exiting a trip

- If a user is not satisfied with the plan and it does not work for them, they can exit the trip.
- After a user exits, agreement is measured among the remaining users only (everyone remaining must approve).
- The trip disappears from the exiting user's trip list.
- Only the primary user is notified when a user exits. The other users are not told.
- Before the user leaves, the agent tries to persuade them to stay, using the same conversational, information-based approach as in 4.4. The user can still leave if they choose.
- If the primary user exits, the trip is cancelled and all users are notified that it has been cancelled.
- An exited user can be re-added to the trip. Only the primary user of that trip can re-add a user. A re-added user sees the earlier chat history. They also keep their earlier answers and choices, and can change any of them.
- When a trip is cancelled, its trip data and chat history are kept (not deleted).
- A cancelled trip can be reopened. Only the primary user of that trip can reopen it. While cancelled, the trip is read-only: no edits, chat, approvals or changes until it is reopened. When a trip is reopened, all users get a notification. Users who had exited before the cancellation do not come back automatically; they stay out until the primary user re-adds them. Only the primary user can see a cancelled trip. Other users are told it was cancelled but cannot open it.

### 4.6 Locking the trip

Trip lifecycle: **Planning** (primary user chats with the agent) → **Finalized and sent** (primary user finalizes and sends to the others) → **Agreement** (questions, change requests, approvals) → **Locked** (consensus reached). A trip can also be **Cancelled** and reopened (4.5).

- Locking is a separate step from finalizing. The primary user finalizes (and thereby sends) a trip, and locks it once the users have reached consensus on the plan.
- Normally the primary user locks the trip once all users agree on the plan.
- The primary user can also lock the trip over a denial. Users who disagree can then leave the trip.
- Only the primary user can lock the trip.
- A locked trip receives no further updates: no changes to the plan, no change requests, no approvals, and no agent nudging.
- Once the trip is locked, all users get to see the final plan of the locked trip.
- All users are notified when the trip is locked.
- The primary user can unlock a locked trip, which returns it to the Agreement phase. Only the primary user can unlock. All users are notified when the trip is unlocked.
- Users can still exit a locked trip (same rules as 4.5), and chat remains available. Chat does not change the plan while the trip is locked.
- On a locked trip the agent can only answer questions. It also tries to persuade users who try to exit to stay (4.5), but does not nudge anyone to change their choices.

## 5. Technical decisions

| Area | Decision | Notes |
| --- | --- | --- |
| Frontend | Angular 20 (`Vamoose.Web`) | Angular CLI project |
| Backend | .NET 9 Web API (`Vamoose.Api`) | Controllers |
| Database | SQL Server Express on AWS (Amazon RDS for SQL Server) | Express edition has no license fee but a 10 GB per-database limit. SQLite rejected because it does not suit AWS hosting. PostgreSQL considered and rejected in favor of SQL Server. |
| Local development database | Remote RDS SQL Server Express | No local database install and no Docker. SQL Server does not run natively on macOS, so all developers (Windows and Mac) connect to AWS. |
| Dev database setup | One database per developer on the shared RDS instance | For example `vamoose_alice`, `vamoose_bob`, so developers do not overwrite each other's data. EF Core migrations run per database. |
| Dev connection string | Kept in user-secrets or local `appsettings.Development.json` | Never committed to git. Each developer uses their own SQL login. |
| Dev network access | RDS security group allows port 1433 from developer IPs | VPN or bastion host preferred over public access. **TBD** |
| AI agents | Strands Agents SDK (Amazon, open source), **Python 3.13** | Implemented in `Vamoose.Agents` (see 5.1). Strands is not available for .NET, so it runs as a separate agent service that `Vamoose.Api` calls over HTTP. Python 3.15 is not supported yet (missing dependency wheels). |
| AI model | Anthropic Claude on Amazon Bedrock, region `us-east-2` | Default model is the Strands SDK default; override with `VAMOOSE_PLANNER_MODEL_ID`. Each developer uses their own IAM user with only Bedrock invoke permissions. |
| Agent hosting | Amazon Bedrock AgentCore Runtime | The service already follows the AgentCore contract. AgentCore needs a linux/arm64 container image, built in AWS (for example CodeBuild), not on developer machines. |
| Hosting | AWS | Other services **TBD** |
| Dev platforms | Windows and macOS | Docker not used. The agent service runs locally in a Python virtual environment. |

### Known tradeoffs of the database choice

- Development requires internet access. No offline work.
- Queries cross the network, so they are slower than a local database.
- The RDS instance costs money while running. Dev instances can be stopped, but RDS restarts them after 7 days.
- Exposing the database to developer IPs needs ongoing security group upkeep.
- Production data must stay out of the shared dev instance.

### 5.1 Agent service (`Vamoose.Agents`)

This is the integration boundary between `Vamoose.Api` and the AI. The shell must be built against it.

**Endpoints** (AgentCore-compatible, port 8080 locally):

- `GET /ping` returns `{"status": "Healthy"}`.
- `POST /invocations` takes one request and returns a stream of events (`text/event-stream`, one `data: {json}` line per event).

**Request** (camelCase JSON):

| Field | Notes |
| --- | --- |
| `requestId` | Lowercase letters, digits and hyphens, 3 to 64 characters |
| `action` | See the table below |
| `message` | The user's text (up to 4000 characters). Untrusted input. |
| `trip` | Trip context (name, destination, dates, traveler count, status). `Chat` without `trip` is the start of a trip (3.1). |
| `plan` | The current plan: slots, each with 1-4 options and the selected index |
| `history` | Recent messages of this user's chat with the agent |
| `canEditPlan` | Whether `Chat` may return an updated plan (primary user, before locking) |
| `nudge` | `Nudge`: kind `Majority` (4.4, with vote counts) or `Stay` (4.5) |
| `responses` | `Analyze`: who approved, denied or hasn't answered, and each participant's choice per slot. Participants are anonymous labels; `Vamoose.Api` maps them back. |

**Events**, in order, each with `requestId` and an increasing `sequence`:

| `type` | Meaning |
| --- | --- |
| `Progress` | A step update (`step`, optional `message`). Zero or more. |
| `Result` | The final output, for example `draft` for the `Draft` action. Exactly one on success. |
| `Error` | A friendly `message` only; details stay in the agent's server logs. Exactly one on failure. |

**Actions:**

| Action | Requirement | Status |
| --- | --- | --- |
| `Draft` | 3.1, 3.4: free-text trip idea to a structured day-by-day draft (`DraftItinerary`: title, destination, dates, group size, rationale, items, assumptions, open questions) | Implemented and tested against real Bedrock |
| `Chat` | 3.1, 3.3: start-of-trip blurb (returns `destination`, `tripName`), then planning with a web search tool; returns `reply` and, when the plan changed, the full `plan` with alternatives | Implemented, mock and offline tests; live smoke tests opt-in |
| `Questions` | 4.1: one question per slot, type `SingleChoice`, with `prompt` and one option label per plan option | Implemented, mock and offline tests; live smoke test opt-in |
| `Analyze` | 4.2: per slot, approvers, dissenters and stored alternatives that apply, plus a summary | Implemented, mock and offline tests; live smoke test opt-in |
| `Nudge` | 4.4 (kind `Majority`) and 4.5 (kind `Stay`) | Implemented, mock and offline tests; live smoke test opt-in |

Unimplemented actions return a single `Error` event ("not available yet"), so the shell can call them safely.

**Configuration** (environment variables):

| Variable | Purpose |
| --- | --- |
| `VAMOOSE_MOCK_AGENTS` | `1` returns canned, repeatable output for every action without calling AWS |
| `Vamoose.Agents/.mock` | If this file exists, the running service switches to mock mode immediately (no restart), for live demos (6.2) |
| `AWS_REGION` | Defaults to `us-east-2` |
| `VAMOOSE_PLANNER_MODEL_ID` | Optional Bedrock model override |
| `AWS_PROFILE` | The developer's AWS credentials profile |
| `AWS_CA_BUNDLE` | Only on networks with TLS inspection (for example Zscaler) |

**Responsibilities:**

- `Vamoose.Api` owns all trip data, users, roles, approvals, nudge counts and persistence. It saves agent results; the agent service stores nothing.
- The agent only suggests. It cannot book, pay, invite or change data directly (see 3.3).

### 5.2 Implementation notes

Decisions made while implementing, where the requirements were open or ambiguous:

- **Magic links (3.2):** every trip member has their own secret link. The organizer is emailed theirs when the trip starts; travelers are emailed theirs when the trip is sent (or when added to an already-sent trip). Opening a link signs the person in, so they can also see their other trips. No email provider is set up yet: in Development the emails are logged and listed at `/dev/outbox`. A real sender (for example Amazon SES) replaces `DevEmailSender`.
- **Start a trip (3.1):** `Chat` without a trip gives the destination blurb; the app then asks "Who are you?" and "Who all is going?". The trip is created with the original prompt as the agent's first message, and the agent replies with questions about the group. The initial plan comes from `Chat` (which keeps alternatives), not `Draft`.
- **24-hour window (1):** each plan version gets 24 hours for responses, starting when the trip is sent and restarting whenever the plan changes. After it closes, responses are refused and only people who responded in time count towards consensus. The organizer can send again to reopen it.
- **Travelers before they log in:** anyone can be added by email; their account exists from then on.
- **Plan items:** the categories and fields in 3.4, with 1-4 options per item.
- **Agent context:** `Vamoose.Api` sends the trip, plan, chat history and responses in each request. The agent stores nothing.
- **Search:** web search uses DuckDuckGo (no API key) behind one tool function, so it can be swapped.
- **Live notifications:** SignalR (`/hubs/notifications`), plus a stored list with unread badge.
- **Demo seed (6.2):** in Development the API seeds "Demo: Lisbon long weekend" (organizer `demo-organizer@vamoose.dev`, three travelers, plan with alternatives). The organizer's magic link is in the dev outbox after every start.

## 6. Non-functional requirements

### 6.1 Testability and demoability of agent features

Every agent feature must meet all of these before it counts as done:

- **Mock mode:** the action returns a fixed, repeatable response when `VAMOOSE_MOCK_AGENTS=1`, so the API and UI can be built and demoed without AWS.
- **Offline tests:** the action is tested with a scripted fake model (no AWS, runs in about a second). See `Vamoose.Agents/tests`.
- **Live smoke test:** one opt-in test runs the action against real Bedrock (`VAMOOSE_LIVE_TESTS=1`).
- **One way in:** `Vamoose.Api` calls the agent service only through an `IAgentClient` interface. API tests use a fake client.
- **All three outcomes handled:** the UI shows `Progress` (loading state), `Result` and `Error` (friendly message with retry). It never shows a blank or stuck screen.
- **Checked output:** agent output is checked against its expected structure before it is saved or shown, and is never rendered as raw HTML.
- **Untrusted input:** user text is passed to the agent as data, never as instructions.
- **Cost caps:** each agent call has a turn limit and an output-token limit.

### 6.2 Demo readiness

- A seeded demo trip exists so the main flow can be shown from a clean start.
- If Bedrock fails during a live demo, the presenter can switch the agent service to mock mode without redeploying.
- The demo path (3.1, 3.3 and the first step of 4.1) works on a phone-sized screen.

### 6.3 Other

**TBD** (performance, security, availability, accessibility, mobile support)

## 7. Open questions

### Functional (to decide)

- Which email provider sends magic links in production (for example Amazon SES)?
- Should the agent use a dedicated flight and hotel data provider instead of general web search?
- Question types beyond single choice (multiple choice, free text, rating)?
- What can travelers do with a received trip? (Deferred to later.)
- Web only, or also a mobile app?

### Technical (decide later)

- Which AWS services will host the frontend and API?
- How will developers reach the RDS instance: public access restricted by IP, VPN, or bastion host?
- ~~Where will the Strands agent service run?~~ Decided: Bedrock AgentCore Runtime (5).
- ~~Python or TypeScript for the agent service, and which model provider?~~ Decided: Python 3.13, Claude on Amazon Bedrock (5).
- ~~How does the agent get trip context?~~ Decided: sent in each request by `Vamoose.Api` (5.2).
- Live notifications use SignalR (WebSockets): the AWS hosting choice must support them.
- Will production use a separate RDS instance from development, and which edition (Express, Web, Standard)?
