import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ChatMessage,
  ChatResponse,
  Choice,
  CreateTrip,
  ExitPreview,
  TripDetail,
  TripSummary,
} from '@interfaces/api';

@Injectable({ providedIn: 'root' })
export class TripService {
  private readonly _http = inject(HttpClient);

  list(): Promise<TripSummary[]> {
    return firstValueFrom(this._http.get<TripSummary[]>('/api/trips'));
  }

  create(trip: CreateTrip): Promise<TripDetail> {
    return firstValueFrom(this._http.post<TripDetail>('/api/trips', trip));
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

  decideChange(id: string, requestId: string, approve: boolean): Promise<TripDetail> {
    return this._post(id, `change-requests/${requestId}/${approve ? 'approve' : 'deny'}`);
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
