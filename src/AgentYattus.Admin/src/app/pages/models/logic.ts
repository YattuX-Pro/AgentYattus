import { AuditFeedEntry, LlmModel, LlmProvider, LlmConnectionKind, LlmSelection, selectionKey } from '../../core/api';

export const MAX_CHAIN = 10;

/** Recherche par nom, identifiant ou fournisseur ; option « gratuits seulement ». */
export function filterModels(models: LlmModel[], query: string, freeOnly: boolean, providerName: (id: string) => string): LlmModel[] {
  const q = query.trim().toLowerCase();
  return models.filter(
    (m) => (!freeOnly || m.free) && (!q || `${m.name} ${m.id} ${m.provider} ${providerName(m.provider)}`.toLowerCase().includes(q)),
  );
}

/** « Tout ajouter » : complète la chaîne avec les modèles gratuits, les locaux d'abord, dix au plus. */
export function fillChain(chain: LlmSelection[], models: LlmModel[], providers: LlmProvider[]): LlmSelection[] {
  const local = new Set(providers.filter((p) => p.kind === LlmConnectionKind.Local).map((p) => p.id));
  const taken = new Set(chain.map(selectionKey));
  const candidates = models
    .filter((m) => m.free && !taken.has(selectionKey(m)))
    .sort((a, b) => Number(local.has(b.provider)) - Number(local.has(a.provider)));
  return [...chain, ...candidates.map((m) => ({ connectionId: m.connectionId, model: m.id }))].slice(0, MAX_CHAIN);
}

/** Monter un modèle d'un cran dans la chaîne. */
export function moveUp(chain: LlmSelection[], index: number): LlmSelection[] {
  if (index <= 0 || index >= chain.length) return chain;
  const next = [...chain];
  [next[index - 1], next[index]] = [next[index], next[index - 1]];
  return next;
}

export interface ChainActivity { takes: number; switchesTo: number; last: string | null }

/**
 * Ce que le journal dit d'un modèle aujourd'hui : combien de fois un agent l'a pris (« modele: »),
 * combien de fois la chaîne a basculé vers lui (« bascule: »), et à quelle heure pour la dernière fois.
 * Les fournisseurs ne disent pas leur quota restant : on montre ce qui s'est réellement passé.
 */
export function chainActivity(model: string, audit: AuditFeedEntry[], now = new Date()): ChainActivity {
  const today = now.toDateString();
  let takes = 0;
  let switchesTo = 0;
  let last: string | null = null;
  for (const e of audit) {
    if (new Date(e.at).toDateString() !== today) continue;
    if (e.type === `modele:${model}`) takes++;
    else if (e.type === `bascule:${model}`) switchesTo++;
    else continue;
    if (!last || e.at > last) last = e.at;
  }
  return { takes, switchesTo, last };
}
