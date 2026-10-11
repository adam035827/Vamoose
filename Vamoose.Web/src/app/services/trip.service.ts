import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ChatMessage,
  ChatResponse,
  Choice,
  ExitPreview,
  OutboxEmail,
  StartIntro,
  StartTrip,
  StartTripResult,
  TripDetail,
  TripMagicLink,
  TripSummary,
} from '@interfaces/api';

@Injectable({ providedIn: 'root' })
export class TripService {
  private readonly _http = inject(HttpClient);

  list(): Promise<TripSummary[]> {
    return firstValueFrom(this._http.get<TripSummary[]>('/api/trips'));
  }

  // Starting a trip is a short conversation with the agent instead of a form (requirements 3.1).
  intro(message: string): Promise<StartIntro> {
    return firstValueFrom(this._http.post<StartIntro>('/api/start/intro', { message }));
  }

  start(request: StartTrip): Promise<StartTripResult> {
    return firstValueFrom(this._http.post<StartTripResult>('/api/start', request));
  }

  outbox(): Promise<OutboxEmail[]> {
    return firstValueFrom(this._http.get<OutboxEmail[]>('/api/dev/outbox'));
  }

  // Organizer only; the API returns 404 when App:ExposeMagicLinks is off.
  magicLinks(id: string): Promise<TripMagicLink[]> {
    return firstValueFrom(this._http.get<TripMagicLink[]>(`/api/trips/${id}/magic-links`));
  }

  get(id: string): Promise<TripDetail> {
    return firstValueFrom(this._http.get<TripDetail>(`/api/trips/${id}`));
  }

  addMember(id: string, email: string): Promise<TripDetail> {
    return firstValueFrom(this._http.post<TripDetail>(`/api/trips/${id}/members`, { email }));
  }

  chat(id: string): Promise<ChatMessage[]> {
    return firstValueFrom(this._http.get<ChatMessage[]>(`/api/trips/${id}/chat`));
  }

  sendMessage(id: string, message: string): Promise<ChatResponse> {
    return firstValueFrom(this._http.post<ChatResponse>(`/api/trips/${id}/chat`, { message }));
  }

  select(id: string, itemId: string, optionId: string): Promise<TripDetail> {
    return firstValueFrom(
      this._http.put<TripDetail>(`/api/trips/${id}/items/${itemId}/selection`, { optionId }),
    );
  }

  send(id: string): Promise<TripDetail> {
    return this._post(id, 'send');
  }

  respond(id: string, choices: Choice[]): Promise<TripDetail> {
    return firstValueFrom(this._http.post<TripDetail>(`/api/trips/${id}/responses`, { choices }));
  }

  exitPreview(id: string): Promise<ExitPreview> {
    return firstValueFrom(this._http.post<ExitPreview>(`/api/trips/${id}/exit/preview`, {}));
  }

  exit(id: string): Promise<unknown> {
    return firstValueFrom(this._http.post(`/api/trips/${id}/exit`, {}));
  }

  reopen(id: string): Promise<TripDetail> {
    return this._post(id, 'reopen');
  }

  lock(id: string): Promise<TripDetail> {
    return this._post(id, 'lock');
  }

  unlock(id: string): Promise<TripDetail> {
    return this._post(id, 'unlock');
  }

  private _post(id: string, action: string): Promise<TripDetail> {
    return firstValueFrom(this._http.post<TripDetail>(`/api/trips/${id}/${action}`, {}));
  }
}
