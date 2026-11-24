import matrix from '../../../shared/rules-matrix.json';

/**
 * Client-side mirror of the round rules, fed by the same fixture the C# tests
 * use. It is only used for previews and the in-game rules sheet: the server is
 * always the authority on the result.
 */
export type CardSymbol = 'X' | '0' | '1' | '2' | '3' | '4' | '5' | '6';
export type LocalOutcome = 'win' | 'lose' | 'draw';

export const CARD_SYMBOLS = matrix.cards as CardSymbol[];

const LOOKUP = new Map<string, LocalOutcome>();
for (const row of matrix.rows) {
  LOOKUP.set(`${row.a}:${row.b}`, row.result === 'a' ? 'win' : row.result === 'b' ? 'lose' : 'draw');
}

export function resolveLocal(yours: string, theirs: string): LocalOutcome {
  return LOOKUP.get(`${yours}:${theirs}`) ?? 'draw';
}

export function isSpecial(card: string): boolean {
  return card === 'X' || card === '0' || card === '4';
}
