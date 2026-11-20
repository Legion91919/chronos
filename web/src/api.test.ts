import { describe, expect, it } from 'vitest';
import { ApiError, sendCommand, type Sender } from './api';

const reply = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

describe('sendCommand', () => {
  it('reuses one idempotency key across retries so a retry cannot book twice', async () => {
    const keys: string[] = [];
    const send: Sender = async (_p, _b, _a, key) => {
      keys.push(key);
      if (keys.length === 1) throw new TypeError('network down');
      if (keys.length === 2) return reply(503, { title: 'Unavailable' });
      return reply(201, { version: 7 });
    };
    const result = await sendCommand('/api/loans/L1/drawdowns', {}, 'ops-17', { send });
    expect(result).toEqual({ version: 7 });
    expect(keys).toHaveLength(3);
    expect(new Set(keys).size).toBe(1);
  });

  it('does not retry a business rule refusal', async () => {
    let calls = 0;
    const send: Sender = async () => { calls++; return reply(409, { code: 'drawdown.four_eyes', detail: 'The person who requested a drawdown cannot approve it.' }); };
    await expect(sendCommand('/x', {}, 'ops-17', { send })).rejects.toBeInstanceOf(ApiError);
    expect(calls).toBe(1);
  });
});
