import { MissionStatus } from './api';

export type Tone = 'success' | 'warning' | 'destructive' | 'secondary';

/** Statut d'une mission : libellé et couleur (vert-de-gris = exécuté, laiton = décision humaine, cinabre = échec). */
export const STATUS: Record<MissionStatus, { label: string; tone: Tone }> = {
  Pending: { label: 'En file', tone: 'secondary' },
  Running: { label: 'En cours', tone: 'success' },
  AwaitingApproval: { label: 'À approuver', tone: 'warning' },
  Completed: { label: 'Terminée', tone: 'secondary' },
  Failed: { label: 'Échouée', tone: 'destructive' },
  Cancelled: { label: 'Arrêtée', tone: 'secondary' },
};

export interface EventView {
  kind: string;
  label: string;
  detail: string;
  tone: Tone;
}

/** Traduit un événement du journal (« outil:read_file », « bascule:nvidia/x », « Running »…) pour un humain. */
export function describe(type: string): EventView {
  const [kind, ...rest] = type.split(':');
  const detail = rest.join(':');
  switch (kind) {
    case 'modele': return { kind, label: 'Modèle', detail, tone: 'secondary' };
    case 'outil': return { kind, label: 'Outil', detail, tone: 'success' };
    case 'action': {
      // « action:analyste:read_file:src/Api.cs » : qui a utilisé quel outil, sur quel chemin (ADR 0012).
      const [role, tool, ...path] = detail.split(':');
      return { kind: 'outil', label: 'Outil', detail: `${tool} · ${path.join(':')} · ${role}`, tone: 'success' };
    }
    case 'bascule': return { kind, label: 'Quota épuisé, bascule', detail, tone: 'warning' };
    case 'decision':
      return detail === 'approuvee'
        ? { kind, label: 'Écriture approuvée', detail: '', tone: 'success' }
        : { kind, label: 'Écriture refusée', detail: '', tone: 'destructive' };
    case 'arret-demande': return { kind: 'decision', label: 'Arrêt demandé', detail: '', tone: 'warning' };
    case 'limite-de-tours': return { kind, label: 'Limite de tours atteinte', detail, tone: 'destructive' };
    case 'usage': {
      const [role, input, output] = detail.split(':');
      return { kind, label: 'Consommation', detail: `${role} · ${Number(input) + Number(output)} tokens`, tone: 'secondary' };
    }
    case 'budget': return { kind, label: 'Budget atteint', detail: 'mission payante refusée', tone: 'destructive' };
  }
  if (type in STATUS) {
    const s = STATUS[type as MissionStatus];
    return { kind: 'statut', label: s.label, detail: '', tone: s.tone };
  }
  return { kind, label: type, detail: '', tone: 'secondary' };
}

/**
 * Le déroulé d'une mission : quand le journal dit qui a fait quoi (« action:… »), la ligne « outil:… » du même appel
 * est redondante ; on ne garde que la plus précise. Les missions plus anciennes n'ont que « outil:… ».
 */
export function timeline<T extends { type: string }>(events: T[]): T[] {
  return events.some((e) => e.type.startsWith('action:')) ? events.filter((e) => !e.type.startsWith('outil:')) : events;
}

// Même règle que le bureau (MissionViewModel.Visible) : catégories Unicode qui ne s'affichent pas ou trompent l'œil,
// plus les sélecteurs de variante et les caractères qui s'affichent vides sans être des espaces.
const HIDDEN_CATEGORY = /^[\p{Cc}\p{Cf}\p{Zl}\p{Zp}\p{Zs}\p{Co}\p{Cn}\p{Cs}]$/u;
const BLANK_LOOKING = new Set([0x034f, 0x115f, 0x1160, 0x17b4, 0x17b5, 0x180e, 0x2800, 0x3164, 0xffa0]);

function isHidden(ch: string): boolean {
  const cp = ch.codePointAt(0)!;
  return HIDDEN_CATEGORY.test(ch) || (cp >= 0xfe00 && cp <= 0xfe0f) || (cp >= 0xe0100 && cp <= 0xe01ef) || BLANK_LOOKING.has(cp);
}

/**
 * Rend visibles les caractères invisibles (zéro largeur, inversion de sens, étiquettes, contrôle…) :
 * un texte à approuver ne doit rien cacher à celui qui le lit. On parcourt par point de code (for…of),
 * pour traiter aussi les caractères hors du plan de base, comme le bloc des étiquettes (U+E0000).
 */
export function reveal(text: string): string {
  let out = '';
  for (const ch of text) {
    if (ch === '\n' || ch === '\t' || ch === ' ') out += ch;
    else if (ch === '\r') out += '␍';
    else if (isHidden(ch)) out += `⟦U+${ch.codePointAt(0)!.toString(16).toUpperCase().padStart(4, '0')}⟧`;
    else out += ch;
  }
  return out;
}

/** Les arguments d'un appel d'outil, un par un, texte brut (les retours à la ligne restent des retours à la ligne). */
export function argumentsOf(json: string): { name: string; value: string }[] {
  try {
    const parsed = JSON.parse(json);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed))
      return Object.entries(parsed).map(([name, v]) => ({ name, value: reveal(typeof v === 'string' ? v : JSON.stringify(v, null, 2)) }));
  } catch {
    // Pas du JSON : on montre le texte tel quel.
  }
  return [{ name: 'arguments', value: reveal(json) }];
}
