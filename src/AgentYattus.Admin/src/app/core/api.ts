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
export interface AgentStats { runs: number; totalTokens: number; averageTokens: number }
export interface Agent {
  name: string; role: string; model: string; maxIterations: number; tools: ToolInfo[]; key: string; modelOverride: LlmSelection | null;
  stats?: AgentStats | null;
  instructions: string; defaultInstructions: string; guardrails: string; version: number; maxIterationsOverride: number | null;
  /** Faux pour un agent créé dans l'admin (ADR 0013). */
  builtin?: boolean;
}
export interface AgentProfile { name: string; summary: string; tools: string[] }
export interface AgentPerformance { role: string; finished: number; completed: number; averageCost: number }
export interface AgentVersion { version: number; instructions: string | null; maxIterations: number | null; at: string; by: string }

export type MissionStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'AwaitingApproval' | 'Cancelled';
export type MissionKind = 'Question' | 'Review';
export interface ToolCall { tool: string; arguments: string }
export interface Mission {
  id: string; goal: string; status: MissionStatus; createdAt: string; result: string | null;
  pendingApproval: { id: string; toolCalls: ToolCall[] } | null; kind: MissionKind;
  projectId?: string | null; projectName?: string | null; createdBy?: string | null;
  inputTokens?: number; outputTokens?: number; cost?: number; retryOf?: string | null;
}

export interface InstanceSettings {
  maxIterations: number; maxDurationMinutes: number; maxConcurrentMissions: number; maxFileKb: number; excludedFiles: string[];
  auditRetentionDays: number; monthlyBudget: number | null; budgetAlertPercent: number; blockAtBudget: boolean;
  environment: { authority: string | null; otlpEndpoint: string | null; defaultWorkspace: string | null; sensitiveTelemetry: boolean };
}
export type UpdateSettings = Omit<InstanceSettings, 'environment'>;
export interface Budget { monthlyBudget: number | null; spent: number; alertPercent: number; blocking: boolean; alert: boolean; reached: boolean }

export type OrganizationRole = 'Admin' | 'Operator' | 'Observer';
export type ProjectVisibility = 'Private' | 'Shared' | 'Organization';
export type ProjectRole = 'Owner' | 'Editor' | 'Reader';
export interface UserRef { id: string; username: string; displayName: string | null }
export interface Membership { organizationId: string; name: string; slug: string; role: OrganizationRole }
export interface Me { id: string; username: string; displayName: string | null; isInstanceAdmin: boolean; organizations: Membership[] }
export interface User {
  id: string; username: string; displayName: string | null; isInstanceAdmin: boolean;
  firstSeenAt: string; lastSeenAt: string; organizations: Membership[];
}
export interface Member { userId: string; username: string; displayName: string | null; role: OrganizationRole }
export interface Organization {
  id: string; name: string; slug: string; defaultVisibility: ProjectVisibility; members: Member[]; myRole: OrganizationRole | null;
}
export interface ProjectMember { userId: string; username: string; displayName: string | null; role: ProjectRole }
export interface Project {
  id: string; name: string; organizationId: string | null; organizationName: string | null; owner: UserRef;
  visibility: ProjectVisibility; isPersonalDefault: boolean; workspace: string | null; myAccess: ProjectRole; canManage: boolean;
  members: ProjectMember[]; createdAt: string;
}
export interface AuditEntry { type: string; at: string }

// Les collègues (ADR 0012) : galerie, traits, répliques, réglages de la scène, équipes des projets.
export interface Portrait { id: string; slug: string; suggestedName: string; active: boolean; hasTalk: boolean; workUrl: string; talkUrl: string }
export interface Trait { key: string; label: string; style: string }
export interface SceneLine { event: string; description: string; speaker: string; template: string; variables: string[]; isDefault: boolean }
export interface DefaultSeat { role: string; portraitId: string; name: string }
export interface TeamSettings { leaderPortraitId: string | null; leaderName: string; managerPortraitId: string | null; defaultTeam: DefaultSeat[]; typingMs: number; pauseMs: number }
export interface Colleague {
  id: string; projectId: string; role: string; portraitId: string; name: string; traits: string[];
  recruitedBy: UserRef | null; recruitedAt: string; active: boolean; removedAt: string | null;
}
export interface ColleagueEntry { colleague: Colleague; projectName: string }
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

  agents() { return this.get<Agent[]>('/agents?all=true'); }
  updateAgent(role: string, instructions: string | null, maxIterations: number | null) {
    return this.send('PUT', `/agents/${encodeURIComponent(role)}`, { instructions, maxIterations });
  }
  createAgent(profile: AgentProfile & { instructions: string }) { return this.send<Agent>('POST', '/agents', profile); }
  updateAgentProfile(role: string, profile: AgentProfile) { return this.send('PUT', `/agents/${encodeURIComponent(role)}/profile`, profile); }
  retireAgent(role: string) { return this.send('DELETE', `/agents/${encodeURIComponent(role)}`); }
  agentPerformance() { return this.get<AgentPerformance[]>('/agents/performance'); }
  agentVersions(role: string) { return this.get<AgentVersion[]>(`/agents/${encodeURIComponent(role)}/versions`); }
  setAgentModel(role: string, model: LlmSelection | null) { return this.send('PUT', `/agents/${encodeURIComponent(role)}/model`, { model }); }

  missions(limit = 100) { return this.get<Mission[]>(`/missions?limit=${limit}`); }
  mission(id: string) { return this.get<Mission>(`/missions/${id}`); }
  events(id: string) { return this.get<AuditEntry[]>(`/missions/${id}/events`); }
  decide(id: string, approvalId: string, approved: boolean) { return this.send('POST', `/missions/${id}/decision`, { approved, approvalId }); }
  cancel(id: string) { return this.send('POST', `/missions/${id}/cancel`); }
  retry(id: string) { return this.send<Mission>('POST', `/missions/${id}/retry`); }
  /** L'export CSV : un fichier, téléchargé avec le jeton (un simple lien ne l'enverrait pas). */
  exportAudit() { return firstValueFrom(this.http.get('/api/audit/export', { responseType: 'blob', observe: 'response' })); }
  audit(limit = 200) { return this.get<AuditFeedEntry[]>(`/audit?limit=${limit}`); }

  settings() { return this.get<InstanceSettings>('/settings'); }
  saveSettings(settings: UpdateSettings) { return this.send('PUT', '/settings', settings); }
  budget() { return this.get<Budget>('/budget'); }

  me() { return this.get<Me>('/me'); }
  users() { return this.get<User[]>('/users'); }
  lookup(q: string) { return this.get<UserRef[]>(`/users/lookup?q=${encodeURIComponent(q)}`); }
  organizations() { return this.get<Organization[]>('/organizations'); }
  createOrganization(name: string, slug: string) { return this.send<Organization>('POST', '/organizations', { name, slug }); }
  updateOrganization(id: string, name: string, defaultVisibility: ProjectVisibility) { return this.send('PUT', `/organizations/${id}`, { name, defaultVisibility }); }
  setOrgMember(id: string, userId: string, role: OrganizationRole) { return this.send('PUT', `/organizations/${id}/members/${encodeURIComponent(userId)}`, { role }); }
  removeOrgMember(id: string, userId: string) { return this.send('DELETE', `/organizations/${id}/members/${encodeURIComponent(userId)}`); }

  projects() { return this.get<Project[]>('/projects'); }
  createProject(name: string, organizationId: string | null, visibility: ProjectVisibility | null) {
    return this.send<Project>('POST', '/projects', { name, organizationId, visibility });
  }
  updateProject(id: string, name: string, visibility: ProjectVisibility) { return this.send('PUT', `/projects/${id}`, { name, visibility }); }
  setProjectMember(id: string, userId: string, role: ProjectRole) { return this.send('PUT', `/projects/${id}/members/${encodeURIComponent(userId)}`, { role }); }
  removeProjectMember(id: string, userId: string) { return this.send('DELETE', `/projects/${id}/members/${encodeURIComponent(userId)}`); }
  setWorkspace(id: string, path: string | null) { return this.send('PUT', `/projects/${id}/workspace`, { path }); }

  portraits(all = false) { return this.get<Portrait[]>(`/team/portraits${all ? '?all=true' : ''}`); }
  /** Un portrait : deux poses envoyées en multipart (le serveur vérifie la signature des fichiers). */
  addPortrait(slug: string, name: string, work: File, talk: File | null) {
    const form = new FormData();
    form.append('slug', slug);
    form.append('name', name);
    form.append('travail', work);
    if (talk) form.append('parle', talk);
    return firstValueFrom(this.http.post<Portrait>('/api/team/portraits', form));
  }
  updatePortrait(id: string, suggestedName: string, active: boolean) { return this.send('PUT', `/team/portraits/${id}`, { suggestedName, active }); }
  deletePortrait(id: string) { return this.send('DELETE', `/team/portraits/${id}`); }
  traits() { return this.get<Trait[]>('/team/traits'); }
  saveTraits(traits: Trait[]) { return this.send('PUT', '/team/traits', { traits }); }
  lines() { return this.get<SceneLine[]>('/team/lines'); }
  saveLines(templates: Record<string, string | null>) { return this.send('PUT', '/team/lines', { templates }); }
  teamSettings() { return this.get<TeamSettings>('/team/settings'); }
  saveTeamSettings(settings: TeamSettings) { return this.send('PUT', '/team/settings', settings); }
  colleagues() { return this.get<ColleagueEntry[]>('/team/colleagues'); }
  removeColleague(projectId: string, id: string) { return this.send('DELETE', `/projects/${projectId}/colleagues/${id}`); }
}

export const VISIBILITY: Record<ProjectVisibility, { label: string; hint: string }> = {
  Private: { label: 'Privé', hint: 'Le propriétaire seul.' },
  Shared: { label: 'Partagé', hint: 'Le propriétaire et les personnes invitées.' },
  Organization: { label: 'Organisation', hint: 'En plus, tous les membres de l’organisation, en lecture.' },
};
export const ORG_ROLE: Record<OrganizationRole, string> = { Admin: 'Administrateur', Operator: 'Opérateur', Observer: 'Observateur' };
export const PROJECT_ROLE: Record<ProjectRole, string> = { Owner: 'Propriétaire', Editor: 'Éditeur', Reader: 'Lecteur' };
