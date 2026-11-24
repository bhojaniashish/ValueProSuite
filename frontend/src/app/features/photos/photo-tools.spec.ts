import { stampLines } from './photo-tools';
import { metres, needsAttention } from './photos.api';

describe('photo stamp', () => {
  it('prints date, time and location, then the case line', () => {
    const [first, second] = stampLines({
      takenAt: new Date(2026, 10, 24, 20, 59), latitude: 23.03025, longitude: 72.52516, accuracyMetres: 6, line: 'VAL/2026/0001 · Om Engineers',
    });
    expect(first).toContain('24 Nov 2026');
    expect(first).toContain('23.030250, 72.525160 (±6 m)');
    expect(second).toBe('VAL/2026/0001 · Om Engineers');
  });

  it('says so when there is no location', () => {
    expect(stampLines({ takenAt: new Date(), latitude: null, longitude: null, line: 'x' })[0]).toContain('Location not available');
  });
});

describe('photos that need a look', () => {
  it('flags far-away photos and site photos without a location, not office uploads', () => {
    expect(needsAttention({ source: 'Site', latitude: 21.1, distanceFromPropertyMetres: 1700 })).toBe('1.7 km from the property');
    expect(needsAttention({ source: 'Site', latitude: null, distanceFromPropertyMetres: null })).toBe('No location');
    expect(needsAttention({ source: 'Upload', latitude: null, distanceFromPropertyMetres: null })).toBeNull();
    expect(needsAttention({ source: 'Site', latitude: 21.1, distanceFromPropertyMetres: 120 })).toBeNull();
    expect(metres(950)).toBe('950 m');
  });
});
