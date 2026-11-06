import { LlmConnectionKind, LlmModel, LlmProvider } from '../../core/api';
import { fillChain, filterModels, moveUp } from './logic';

const model = (provider: string, id: string, free: boolean): LlmModel => ({
  connectionId: provider, provider, id, name: id, contextWindow: null,
  inputPricePerMillion: free ? 0 : 1, outputPricePerMillion: free ? 0 : 2, reasoning: false, free,
});
const providers: LlmProvider[] = [
  { id: 'ollama', name: 'Ollama', kind: LlmConnectionKind.Local, defaultBaseUrl: null, modelCount: 0, notice: null },
  { id: 'openrouter', name: 'OpenRouter', kind: LlmConnectionKind.ApiKey, defaultBaseUrl: null, modelCount: 0, notice: null },
];
const models = [model('openrouter', 'gemma:free', true), model('anthropic', 'claude-haiku-5-5', false), model('ollama', 'qwen3:8b', true)];

describe('logique des modèles', () => {
  it('cherche par nom ou fournisseur, et filtre les gratuits', () => {
    const name = (id: string) => providers.find((p) => p.id === id)?.name ?? id;
    expect(filterModels(models, 'ollama', false, name).map((m) => m.id)).toEqual(['qwen3:8b']);
    expect(filterModels(models, '', true, name).map((m) => m.id)).toEqual(['gemma:free', 'qwen3:8b']);
  });

  it('« Tout ajouter » ne prend que des gratuits, les locaux d’abord, sans doublon', () => {
    const chain = fillChain([{ connectionId: 'openrouter', model: 'gemma:free' }], models, providers);
    expect(chain.map((s) => s.model)).toEqual(['gemma:free', 'qwen3:8b']);
  });

  it('monte un modèle d’un cran', () => {
    const chain = [{ connectionId: 'a', model: '1' }, { connectionId: 'b', model: '2' }];
    expect(moveUp(chain, 1).map((s) => s.model)).toEqual(['2', '1']);
    expect(moveUp(chain, 0)).toBe(chain);
  });
});
