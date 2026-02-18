# Contributing Guidelines

Welcome to the PDF Editor project! These guidelines will help ensure smooth collaboration.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR-USERNAME/PDF-Editor.git`
3. Add upstream: `git remote add upstream https://github.com/OriolCanillasGautier/PDF-Editor.git`
4. Create feature branch: `git checkout -b feature/your-feature-name`

## Development Workflow

### 1. Before Starting Work
- Check open issues and pull requests to avoid duplication
- Create an issue for your feature/bug fix
- Link your branch to the issue

### 2. Code Style
- Follow C# naming conventions (PascalCase for public members)
- Use meaningful variable names
- Add XML documentation comments to public APIs
- Keep lines under 120 characters

### 3. Commit Messages
```
[TYPE] Brief description

More detailed explanation if needed.
Closes #123
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`

### 4. Pull Request Process
1. Update CHANGELOG.md
2. Add/update tests
3. Ensure all tests pass: `dotnet test`
4. Update documentation if needed
5. Submit PR with clear description

## Testing Requirements

- All new features must have unit tests
- Tests must pass locally: `dotnet test`
- Aim for >80% code coverage
- Run `dotnet test --logger:trx` for detailed results

## Documentation

- Update relevant docs for new features
- Add examples for complex functionality
- Update API documentation comments
- Keep SETUP.md current

## Reporting Issues

Use this template:

```
## Description
Clear description of the issue

## Steps to Reproduce
1. ...
2. ...
3. ...

## Expected Behavior
What should happen

## Actual Behavior
What actually happens

## Environment
- OS: Windows/Linux
- .NET Version: 6.0/7.0
- PDF Editor Version: 0.x.x
```

## Project Structure Rules

- Don't move or delete existing files without discussion
- New features in their own folder (e.g., `Services/OcrService.cs`)
- Keep interfaces in `Abstractions/`
- Tests mirror source structure

## Code Review Checklist

Before submitting a PR, ensure:
- [ ] Follows code style guidelines
- [ ] Includes meaningful commit messages
- [ ] Has appropriate unit tests
- [ ] Documentation is updated
- [ ] No breaking changes (or documented)
- [ ] Builds successfully
- [ ] No unnecessary dependencies added

## Questions?

- Check existing documentation first
- Open a discussion in the repository
- Comment on related issues
- Email project maintainer

Thank you for contributing!
