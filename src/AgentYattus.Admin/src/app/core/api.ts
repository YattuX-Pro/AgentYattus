import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Recopie des contrats C# (src/AgentYattus.Contracts). À garder alignés ; OpenAPI les générera si l'écart devient pénible.

export enum LlmConnectionKind { ApiKey = 0, Local = 1, Custom = 2 }
export interface LlmProvider { id: string; name: string; kind: LlmConnectionKind; defaultBaseUrl: string | null; modelCount: number; notice: string | null }
export interface LlmConnection {
  id: string; provider: string; providerName: string; label: string; last4: string | null; baseUrl: string | null;
  createdAt: string; createdBy: string; revokedAt: string | null;
}
export interface LlmModel {
  connectionId: string; provider: string; id: string; name: string; contextWindow: number | null;
  inputPricePerMillion: number | null; outputPricePerMillion: number | null; reasoning: boolean; free: boolean;
}
export interface LlmSelection { connectionId: string; model: string }
export interface LlmModels { models: LlmModel[]; errors: { connectionId: string; message: string }[] }
export interface LlmStatus {
  providers: LlmProvider[]; connections: LlmConnection[]; default: LlmSelection | null; freeMode: boolean; freeChain: LlmSelection[] | null;
}
export interface AddConnection { provider: string; label: string; apiKey: string | null; baseUrl: string | null }

export interface ToolInfo { name: string; description: string; requiresApproval: boolean }
export interface Agent { name: string; role: string; model: string; maxIterations: number; tools: ToolInfo[]; key: string; modelOverride: LlmSelection | null }

export type MissionStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'AwaitingApproval';
export type MissionKind = 'Question' | 'Review';
export interface ToolCall { tool: string; arguments: string }
export interface Mission {
  id: string; goal: string; status: MissionStatus; createdAt: string; result: string | null;
  pendingApproval: { id: string; toolCalls: ToolCall[] } | null; kind: MissionKind;
}
export interface AuditEntry { type: string; at: string }
export interface AuditFeedEntry { missionId: string; type: string; at: string }

/** Un modèle choisi s'identifie par sa connexion et son nom. */
export const selectionKey = (s: { connectionId: string; model?: string; id?: string } | null | undefined) =>
  s ? `${s.connectionId}|${s.model ?? s.id}` : '';

/** Le message lisible d'une erreur d'API (ProblemDetails de validation compris). */
export function errorMessage(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    const errors = e.error?.errors as Record<string, string[]> | undefined;
    if (errors) return Object.values(errors).flat().join(' ');
    if (e.status === 0) return 'L’API ne répond pas.';
    if (e.status === 403) return 'Ce compte n’a pas le droit de faire cela.';
    if (e.status === 409) return 'Quelqu’un a déjà décidé, ou la demande a changé. Rechargez.';
    return e.error?.title ?? `Erreur ${e.status}.`;
  }
  return 'Erreur inattendue.';
}

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);
  private get<T>(url: string) { return firstValueFrom(this.http.get<T>(`/api${url}`)); }
  private send<T = void>(method: 'POST' | 'PUT' | 'DELETE', url: string, body?: unknown) {
    return firstValueFrom(this.http.request<T>(method, `/api${url}`, { body }));
  }

  llm() { return this.get<LlmStatus>('/llm'); }
  models() { return this.get<LlmModels>('/llm/models'); }
  connect(request: AddConnection) { return this.send('POST', '/llm/connections', request); }
  revoke(id: string) { return this.send('DELETE', `/llm/connections/${id}`); }
  setDefault(selection: LlmSelection) { return this.send('PUT', '/llm/settings', { default: selection }); }
  setFree(enabled: boolean, chain: LlmSelection[]) { return this.send('PUT', '/llm/free', { enabled, chain }); }

  agents() { return this.get<Agent[]>('/agents'); }
  setAgentModel(role: string, model: LlmSelection | null) { return this.send('PUT', `/agents/${encodeURIComponent(role)}/model`, { model }); }

  missions(limit = 100) { return this.get<Mission[]>(`/missions?limit=${limit}`); }
  mission(id: string) { return this.get<Mission>(`/missions/${id}`); }
  events(id: string) { return this.get<AuditEntry[]>(`/missions/${id}/events`); }
  decide(id: string, approvalId: string, approved: boolean) { return this.send('POST', `/missions/${id}/decision`, { approved, approvalId }); }
  audit(limit = 200) { return this.get<AuditFeedEntry[]>(`/audit?limit=${limit}`); }
}
