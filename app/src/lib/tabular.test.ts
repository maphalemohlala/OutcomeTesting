import { describe, expect, it } from 'vitest';
import { buildCsv, quoteCsv } from './tabular';

const line = (cell: string | number) => buildCsv(['H'], [[cell]]).split('\r\n')[1];

describe('a CSV cell', () => {
  it('cannot arrive in Excel as a formula', () => {
    expect(line('=HYPERLINK("http://bad.example")')).toBe(`"'=HYPERLINK(""http://bad.example"")"`);
    expect(line('@SUM(A1)')).toBe(`'@SUM(A1)`);
    expect(line('+441234')).toBe(`'+441234`);
    expect(line('-1+1')).toBe(`'-1+1`);
  });

  it('leaves ordinary text and real numbers alone', () => {
    expect(line('Mrs A Smith')).toBe('Mrs A Smith');
    expect(line(-12)).toBe('-12');
  });

  it('still quotes commas', () => {
    expect(line('Smith, A')).toBe('"Smith, A"');
  });
});

describe('quoteCsv, for text the server parses', () => {
  it('quotes but never alters what the file said', () => {
    expect(quoteCsv('=1+1')).toBe('=1+1');
    expect(quoteCsv('a,b')).toBe('"a,b"');
  });
});
