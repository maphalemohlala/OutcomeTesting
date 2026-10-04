import { describe, expect, it } from 'vitest';
import {
  emailDirectoryNote,
  isEmail,
  personRefusals,
  sharedEmailNote,
  staleEmailAfterRename,
  withPersonPairs,
} from './casePeople';

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

describe('staleEmailAfterRename', () => {
  it('clears a stale email when the picker could not resolve the new name', () => {
    expect(
      staleEmailAfterRename('Adam Strumidlo', 'adam.strumidlo@example.com', 'Someone Else', 'adam.strumidlo@example.com'),
    ).toBe(true);
  });

  it('ignores case and spaces, so the same person retyped is not a rename', () => {
    expect(
      staleEmailAfterRename('Adam Strumidlo', 'adam.strumidlo@example.com', '  adam strumidlo ', 'adam.strumidlo@example.com'),
    ).toBe(false);
  });

  it('leaves an email the user already typed themselves alone', () => {
    expect(
      staleEmailAfterRename('Adam Strumidlo', 'adam.strumidlo@example.com', 'Someone Else', 'someone.else@example.com'),
    ).toBe(false);
  });

  it('does nothing when the name was not actually changed', () => {
    expect(
      staleEmailAfterRename('Adam Strumidlo', 'adam.strumidlo@example.com', 'Adam Strumidlo', 'adam.strumidlo@example.com'),
    ).toBe(false);
  });
});

describe('emailDirectoryNote', () => {
  const directory = ['sam@example.com', 'pip@example.com'];

  it('is null for a blank email', () => {
    expect(emailDirectoryNote('', directory)).toBeNull();
  });

  it('is null when exactly one active person holds the email', () => {
    expect(emailDirectoryNote('SAM@example.com', directory)).toBeNull();
  });

  it('warns when no active person holds the email', () => {
    expect(emailDirectoryNote('nobody@example.com', directory)).toBe(
      'No active person in the directory has this email, so nobody will be able to answer '
        + "this case's remediation until they are added.",
    );
  });

  it('warns differently when two active people share the email', () => {
    expect(emailDirectoryNote('sam@example.com', [...directory, 'SAM@example.com'])).toBe(
      'Two people in the directory share the email sam@example.com, so nobody can be chosen. Remove the duplicate.',
    );
  });
});

describe('sharedEmailNote', () => {
  it('is null for a blank email', () => {
    expect(sharedEmailNote('', ['sam@example.com', 'SAM@example.com'])).toBeNull();
  });

  it('is null when at most one active person holds the email', () => {
    expect(sharedEmailNote('sam@example.com', ['sam@example.com', 'pip@example.com'])).toBeNull();
  });

  it('names the email when two active people share it, case-insensitively', () => {
    expect(sharedEmailNote('sam@example.com', ['sam@example.com', 'SAM@example.com'])).toBe(
      'Two people in the directory share the email sam@example.com, so nobody can be chosen. Remove the duplicate.',
    );
  });
});
