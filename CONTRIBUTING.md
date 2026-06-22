# Contributing to CNCSS

## Сборка

```powershell
dotnet build CNCSS.sln -c Release
dotnet test CNCSS.sln -c Release
```

## Архитектура

См. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Зависимости: `App → Visualization → Logic → Data`.

## PR checklist

- [ ] Public API — XML `<summary>`
- [ ] Нет magic numbers вне `Data/Config`
- [ ] Инварианты заготовки в `AGENTS.md` не нарушены
- [ ] `scripts/check-file-loc.ps1` без новых превышений (или обоснование)
