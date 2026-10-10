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

### 3.4 Trip details

A trip contains:

- Flight details
- Hotel details
- Restaurant details
- Other users included in the trip
- Planned activities

Exact fields for each of these are **TBD**.

### 3.5 Finalize and send trip

- When the trip details are final, the primary user sends the trip to all other users in the trip.
- Other travelers are added to a trip by entering their email addresses.
- The finalized trip is delivered in-app only. A traveler sees it after logging in with their email. No email notification is sent.
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

### 4.2 Plan evaluation

- After the users answer, the agent gathers everyone's responses and decides whether the plan works for all users.
- The plan works for all users only when **everyone approves** it (unanimous). Any dissent triggers alternatives and nudging.
- If a user does not like something in the plan, they see alternatives.
- Alternatives come from the original trip plan, so the agent must keep alternatives for each item (flights, hotels, restaurants, activities) while planning in Part 1. The agent does not invent new alternatives at this stage.

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
| AI agents | Strands Agents SDK (Amazon, open source) | Available for Python and TypeScript, not .NET. Runs as a separate agent service that `Vamoose.Api` calls over HTTP. Agent language, hosting (Lambda, ECS or Bedrock AgentCore) and model provider (for example Amazon Bedrock) **TBD**. |
| Hosting | AWS | Other services **TBD** |
| Dev platforms | Windows and macOS | Docker not used |

### Known tradeoffs of the database choice

- Development requires internet access. No offline work.
- Queries cross the network, so they are slower than a local database.
- The RDS instance costs money while running. Dev instances can be stopped, but RDS restarts them after 7 days.
- Exposing the database to developer IPs needs ongoing security group upkeep.
- Production data must stay out of the shared dev instance.

## 6. Non-functional requirements

**TBD** (performance, security, availability, accessibility, mobile support)

## 7. Open questions

### Functional (to decide)

- What fields does a trip have at creation (name, destination, dates, budget)?
- What fields do flight, hotel, restaurant and activity entries have?
- Which internet search tool or API will the agent use (for example a search API, or flight and hotel data providers)?
- How are the preference questions generated and answered (types, how many, can users skip)?
- Does each trip item (flight, hotel, restaurant, activity) have its own alternatives, and how many?
- Can a user be added to a trip before they have ever logged in?
- What can travelers do with a received trip? (Deferred to later.)
- Web only, or also a mobile app?

### Technical (decide later)

- Which AWS services will host the frontend and API?
- How will developers reach the RDS instance: public access restricted by IP, VPN, or bastion host?
- Where will the Strands agent service run (Lambda, ECS, Bedrock AgentCore)?
- Python or TypeScript for the agent service, and which model provider?
- Does the live notification feature need WebSockets/SignalR hosted on AWS (affects hosting choice)?
- Will production use a separate RDS instance from development, and which edition (Express, Web, Standard)?
