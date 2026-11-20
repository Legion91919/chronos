import { describe, expect, it } from 'vitest';
import { describeProblem, formatMoney, nextBookableDate, sumOfEffects, toViewState, type StatementLine } from './ledger';

const line = (balanceEffect: number, kind = 'X'): StatementLine => ({
  version: 1, date: '2026-01-01', kind, reference: '', description: '', amount: Math.abs(balanceEffect), balanceEffect,
  principalAfter: 0, interestAfter: 0, outstandingAfter: 0, actor: 'ops-1',
});

describe('formatMoney', () => {
  it('groups thousands, keeps two decimals and uses a real minus sign', () => {
    expect(formatMoney(783178.08)).toBe('783,178.08');
    expect(formatMoney(-20000)).toBe('−20,000.00');
    expect(formatMoney(3178.08, 'GBP', true)).toBe('+3,178.08');
  });
  it('respects currencies without minor units', () => {
    expect(formatMoney(1500, 'JPY')).toBe('1,500');
  });
});

describe('sumOfEffects', () => {
  it('is the outstanding balance, exact to the cent (no float drift)', () => {
    const lines = [line(0, 'LoanOpened'), line(0, 'DrawdownRequested'), line(800000), line(3178.08), line(-20000), line(0.1), line(0.2)];
    expect(sumOfEffects(lines)).toBe(783178.38);
  });
  it('ignores pending drawdowns because their effect is zero', () => {
    expect(sumOfEffects([line(0, 'DrawdownRequested')])).toBe(0);
  });
});

describe('toViewState', () => {
  it('derives the four screen states', () => {
    expect(toViewState(true, null, null, () => false).kind).toBe('loading');
    expect(toViewState(false, 'boom', null, () => false)).toEqual({ kind: 'error', message: 'boom' });
    expect(toViewState(false, null, [], (d: number[]) => d.length === 0).kind).toBe('empty');
    expect(toViewState(false, null, [1], (d: number[]) => d.length === 0)).toEqual({ kind: 'success', data: [1] });
  });
  it('explains problems with a short correlation reference', () => {
    expect(describeProblem({ detail: 'The person who requested a drawdown cannot approve it.', correlationId: '518ff399be2e7a17' }, 'x'))
      .toBe('The person who requested a drawdown cannot approve it. (ref 518ff399)');
  });
});

describe('nextBookableDate', () => {
  it('moves past a closed interest period', () => {
    expect(nextBookableDate({ asOf: '2026-01-31', accruedThrough: '2026-01-31' })).toBe('2026-02-01');
    expect(nextBookableDate({ asOf: '2026-02-10', accruedThrough: '2026-01-31' })).toBe('2026-02-10');
    expect(nextBookableDate({ asOf: '2026-02-10', accruedThrough: null })).toBe('2026-02-10');
  });
});
