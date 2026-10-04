import { describe, expect, it } from 'vitest';
import { isEmail, personRefusals, withPersonPairs } from './casePeople';

describe('isEmail', () => {
  it.each([
    ['sam@example.com', true],
    ['  sam.o’neil@sub.example.co.uk ', true],
    ['Sam Adviser', false],
    ['sam@', false],
    ['sam@example', false],
    ['', false],
    [null, false],
  ])('%s -> %s', (value, expected) => {
    expect(isEmail(value)).toBe(expected);
  });
});

describe('withPersonPairs', () => {
  const form = {
    al_advisername: 'Adam Strumidlo',
    al_adviseremail: 'adam.strumidlo@example.com',
    al_paraplanner: 'Pip Planner',
    al_paraplanneremail: 'pip@example.com',
  };

  it('sends the email with a changed name', () => {
    expect(withPersonPairs({ al_advisername: 'Adam Strumidlo' }, form)).toEqual({
      al_advisername: 'Adam Strumidlo',
      al_adviseremail: 'adam.strumidlo@example.com',
    });
  });

  it('sends the name with a changed email', () => {
    expect(withPersonPairs({ al_paraplanneremail: 'pip@example.com' }, form)).toEqual({
      al_paraplanner: 'Pip Planner',
      al_paraplanneremail: 'pip@example.com',
    });
  });

  it('leaves an unrelated change alone', () => {
    expect(withPersonPairs({ al_clientname: 'Mr Client' }, form)).toEqual({ al_clientname: 'Mr Client' });
  });
});

describe('personRefusals', () => {
  it('refuses a changed name with no email', () => {
    expect(
      personRefusals(
        { al_advisername: 'Sam Adviser' },
        { al_advisername: 'Sam Adviser', al_adviseremail: '', al_paraplanner: '', al_paraplanneremail: '' },
      ),
    ).toEqual(["Give the adviser's email as well as their name."]);
  });

  it('refuses an email that is not one', () => {
    expect(
      personRefusals(
        { al_paraplanneremail: 'Pip Planner' },
        { al_advisername: '', al_adviseremail: '', al_paraplanner: 'Pip', al_paraplanneremail: 'Pip Planner' },
      ),
    ).toEqual(['The paraplanner\'s email "Pip Planner" is not an email address.']);
  });

  it('ignores a legacy case whose people were not touched', () => {
    expect(
      personRefusals(
        { al_clientname: 'Mr Client' },
        { al_advisername: 'Sam', al_adviseremail: '', al_paraplanner: '', al_paraplanneremail: '' },
      ),
    ).toEqual([]);
  });
});
