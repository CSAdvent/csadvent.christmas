# Contributing to C# Advent

Thank you for your interest in contributing! This repository is the site behind [csadvent.christmas](https://csadvent.christmas). If you'd like to write a post for the calendar instead, sign up on the site while sign-ups are open.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting Started

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR_USERNAME/csadvent.christmas.git
   cd csadvent.christmas
   ```
3. Add the upstream remote:
   ```bash
   git remote add upstream https://github.com/CSAdvent/csadvent.christmas.git
   ```
4. Install the .NET SDK named in `global.json`, Docker (PostgreSQL and MailPit run in containers), and the [Aspire CLI](https://aspire.dev)
5. Run the site:
   ```bash
   dotnet tool restore
   aspire run
   ```
   Aspire starts the site, PostgreSQL, the database migrations, pgweb and MailPit, and its dashboard links to each. Locally you can sign in without GitHub at `/dev/signin?login=your-name&id=1`, and every email the site sends lands in MailPit.

## Code Style and Conventions

- Read [CLAUDE.md](../CLAUDE.md) first. It's the project's guide to how everything works, and the rules that keep it working (every page renders statically on the server, for example, with no inline scripts)
- Write code that reads like the code around it: its naming, its idioms and its comments
- Use meaningful variable and method names, and keep methods focused

## Branch Naming Conventions

Use the format: `type/scope/short-description`

Examples:
- `feat/calendar/countdown`
- `fix/claims/duplicate-request`
- `docs/runbook/december`

## Commit Message Format

We use [Conventional Commits](https://www.conventionalcommits.org/). Format:

```
type(scope): description
```

The first line can be at most 72 characters. Every commit and the pull request's title are checked when you open a pull request.

### Commit Types

| Type | Description |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `style` | Formatting, with no change in behavior |
| `refactor` | Code change that neither fixes a bug nor adds a feature |
| `perf` | Performance improvement |
| `test` | Adding or updating tests |
| `build` | Build system or dependencies |
| `ci` | CI/CD changes |
| `chore` | Maintenance tasks |
| `revert` | Reverts an earlier commit |

### Examples

```
feat(calendar): count down to the next reveal
fix(claims): report a duplicate request as already requested
docs(runbook): explain the December schema freeze
```

## Submitting Pull Requests

### Before You Start

1. Check existing issues and PRs to avoid duplicate work
2. For significant changes, open an issue first to discuss your approach
3. Create a new branch from an updated `main` branch

### Pull Request Process

1. Update your fork:
   ```bash
   git checkout main
   git pull upstream main
   ```

2. Create a feature branch:
   ```bash
   git checkout -b feat/scope/your-feature-name
   ```

3. Make your changes and commit using conventional commit format

4. Push to your fork:
   ```bash
   git push origin feat/scope/your-feature-name
   ```

5. Open a pull request against `main`

### Pull Request Guidelines

- Use conventional commit format for the PR title (e.g., `feat(scope): description`)
- Provide a clear description of the changes
- Reference any related issues in the description (e.g., "Closes #123"), not in commit messages
- Ensure the build and tests pass
- Keep PRs focused - one feature or fix per PR

## Testing Requirements

- New and changed code needs tests: unit and component tests (xUnit and bUnit) in `tests/CSAdvent.UnitTests`, and tests that start the whole site in `tests/CSAdvent.IntegrationTests`
- `dotnet build CSAdvent.slnx` and `dotnet test` must pass before you commit. The integration tests need Docker; `dotnet test --project tests/CSAdvent.UnitTests` runs only the unit tests
- Tests must pass in any order and alone, so each one creates the data it reads (CLAUDE.md says how)

## Database Changes

- Add a migration with `dotnet ef migrations add <Name> --project src/CSAdvent.Web --output-dir Data/Migrations`
- Each migration must still work with the code before it, because the site keeps running while migrations apply: add columns and tables first, and remove them in a later release
- No migrations from December 1 to 26, while the calendar is live

## Getting Help

- Open an issue for bugs or feature requests
- For questions about your own spot or a post on the calendar, email [hello@csadvent.christmas](mailto:hello@csadvent.christmas) rather than opening an issue, since the repository is public

## License

By contributing, you agree that your contributions will be licensed under the project's [MIT License](../LICENSE).
