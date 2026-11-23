import { HttpErrorResponse } from '@angular/common/http';
import { toProblem } from '../api/problem';
import { avatarColor, initials, timeAgo } from './format';

describe('format helpers', () => {
  const now = new Date('2026-11-23T12:00:00Z');

  it('describes how long ago something happened', () => {
    expect(timeAgo(null, now)).toBe('Never');
    expect(timeAgo('2026-11-23T11:58:00Z', now)).toBe('2 min ago');
    expect(timeAgo('2026-11-23T09:00:00Z', now)).toBe('3 hr ago');
    expect(timeAgo('2026-11-22T10:00:00Z', now)).toBe('Yesterday');
  });

  it('builds initials and a stable avatar colour', () => {
    expect(initials('Sneha  Kulkarni')).toBe('SK');
    expect(initials('HDFC - Vesu Branch')).toBe('HV');
    expect(avatarColor('Neha Joshi')).toBe(avatarColor('Neha Joshi'));
  });
});

describe('toProblem', () => {
  it('maps validation errors to camelCase fields', () => {
    const p = toProblem(
      new HttpErrorResponse({ status: 400, error: { title: 'Invalid', errors: { FullName: ['Required.'], '$.status': ['Bad'] } } }),
    );
    expect(p.fields).toEqual({ fullName: 'Required.', status: 'Bad' });
  });

  it('maps a business conflict to its field', () => {
    const p = toProblem(new HttpErrorResponse({ status: 409, error: { title: 'Email exists', field: 'email' } }));
    expect(p).toEqual({ status: 409, message: 'Email exists', fields: { email: 'Email exists' } });
  });

  it('explains a network failure', () => {
    expect(toProblem(new HttpErrorResponse({ status: 0 })).message).toContain('Could not reach the server');
  });
});
