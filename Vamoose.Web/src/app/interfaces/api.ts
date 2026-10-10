export type TripStatus = 'Planning' | 'Agreement' | 'Locked' | 'Cancelled';
export type MemberRole = 'Primary' | 'Traveler';
export type PlanCategory = 'Flight' | 'Hotel' | 'Restaurant' | 'Activity';

export interface User {
  id: string;
  email: string;
}

export interface TripSummary {
  id: string;
  name: string;
  destination: string;
  startDate: string | null;
  endDate: string | null;
  status: TripStatus;
  myRole: MemberRole;
  primaryEmail: string;
  memberCount: number;
}

export interface Member {
  userId: string;
  email: string;
  role: MemberRole;
  status: 'Active' | 'Exited';
  approval: 'Approved' | 'Denied' | null;
}

export interface PlanOption {
  id: string;
  title: string;
  description: string | null;
  provider: string | null;
  location: string | null;
  price: number | null;
  currency: string | null;
  url: string | null;
  startTime: string | null;
  endTime: string | null;
  votes: number;
}

export interface PlanItem {
  id: string;
  key: string;
  category: PlanCategory;
  title: string;
  day: number | null;
  selectedOptionId: string | null;
  question: string | null;
  myChoiceOptionId: string | null;
  options: PlanOption[];
}

export interface ChangeRequest {
  id: string;
  itemId: string;
  itemTitle: string;
  requestedByEmail: string;
  fromTitle: string;
  toTitle: string;
  status: 'Pending' | 'Approved' | 'Denied' | 'Superseded';
  createdAt: string;
}

export interface Permissions {
  canChat: boolean;
  canEditPlan: boolean;
  canAddMembers: boolean;
  canSend: boolean;
  canRespond: boolean;
  canLock: boolean;
  canUnlock: boolean;
  canReopen: boolean;
  canExit: boolean;
}

export interface TripDetail {
  id: string;
  name: string;
  destination: string;
  startDate: string | null;
  endDate: string | null;
  notes: string | null;
  status: TripStatus;
  planVersion: number;
  myRole: MemberRole;
  primaryEmail: string;
  myApproval: 'Approved' | 'Denied' | null;
  consensus: boolean;
  members: Member[];
  items: PlanItem[];
  changeRequests: ChangeRequest[];
  permissions: Permissions;
}

export interface CreateTrip {
  name: string;
  destination: string;
  startDate: string | null;
  endDate: string | null;
  notes: string | null;
}

export interface ChatMessage {
  id: string;
  role: 'User' | 'Agent';
  content: string;
  createdAt: string;
}

export interface ChatResponse {
  messages: ChatMessage[];
  planUpdated: boolean;
}

export interface Choice {
  itemId: string;
  optionId: string;
}

export interface ExitPreview {
  message: string;
  cancelsTrip: boolean;
}

export interface AppNotification {
  id: string;
  tripId: string | null;
  type: string;
  message: string;
  createdAt: string;
  read: boolean;
}

export interface ProblemDetails {
  status?: number;
  detail?: string;
}
