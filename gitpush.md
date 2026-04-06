# gitpush rules — pgwiz

> strict commit hygiene policy. no exceptions.

---

## ✅ DO

- always commit as yourself — `pgwiz` is the only valid author
- always include this co-author line on every commit:
  ```
  Co-Authored-By: pgwiz <pgwiz@users.noreply.github.com>
  ```
- write clean, intentional commit messages
- verify your git identity before pushing to any repo:
  ```bash
  git config user.name
  git config user.email
  ```

---

## ❌ DON'T

- never allow AI tools to inject co-author lines automatically. these are all **banned**:
  ```
  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
  Co-authored-by: Claude <noreply@anthropic.com>
  Co-authored-by: GitHub Copilot <copilot@github.com>
  Co-authored-by: assistant <assistant@anthropic.com>
  ```
- never push commits with any `anthropic`, `copilot`, `openai`, `claude`, or `github-actions[bot]` in the co-author line
- never use `--allow-empty` commits with AI-generated messages
- never let VS Code, Rider, or any IDE auto-populate commit messages from AI suggestions without reviewing them first
- never rebase without checking that AI co-author lines weren't introduced mid-history

---

## 🔍 how to check before pushing

scan staged commit message for banned co-authors:
```bash
git log --oneline -10 | head
git log -1 --pretty=full
```

search entire repo history for AI co-author pollution:
```bash
git log --all --pretty="%H %s %b" | grep -i "co-authored-by" | grep -iE "copilot|anthropic|claude|openai|github-actions"
```

---

## 🧹 how to fix if already committed

**amend the last commit:**
```bash
git commit --amend
# remove the Co-authored-by line manually in the editor
git push --force-with-lease
```

**fix deeper in history (interactive rebase):**
```bash
git rebase -i HEAD~5
# mark offending commits as 'reword' or 'edit'
# remove the co-author line, save, continue
git rebase --continue
git push --force-with-lease
```

**strip all AI co-author lines from entire history:**
```bash
git filter-branch --msg-filter '
  sed "/Co-authored-by:.*copilot\|Co-authored-by:.*anthropic\|Co-authored-by:.*claude\|Co-authored-by:.*openai/Id"
' -- --all
git push --force
```

---

## ⚙️ enforce via git hook

> **the hooks path is permanently fixed to `$env:USERPROFILE\.git-hooks` on Windows. never change this path. never move it. never override it per-repo.**

### setup — run once on Windows (PowerShell)

```powershell
# 1. create the global hooks folder — fixed location, never change
New-Item -ItemType Directory -Force -Path "$env:USERPROFILE\.git-hooks"

# 2. write the hook (blocks banned authors + adds required co-author)
@'
#!/bin/sh
COMMIT_MSG_FILE=$1
BANNED="copilot\|anthropic\|claude\|openai\|github-actions"
REQUIRED_COAUTHOR="Co-Authored-By: pgwiz <pgwiz@users.noreply.github.com>"

# Block banned co-authors
if grep -qi "co-authored-by.*\($BANNED\)" "$COMMIT_MSG_FILE"; then
  echo ""
  echo "  [pgwiz] banned co-author detected."
  echo ""
  exit 1
fi

# Add required co-author if not present
if ! grep -qi "Co-Authored-By: pgwiz" "$COMMIT_MSG_FILE"; then
  echo "" >> "$COMMIT_MSG_FILE"
  echo "$REQUIRED_COAUTHOR" >> "$COMMIT_MSG_FILE"
fi
'@ | Set-Content "$env:USERPROFILE\.git-hooks\commit-msg" -Encoding UTF8

# 3. register globally — applies to every repo on this machine
git config --global core.hooksPath "$env:USERPROFILE\.git-hooks"
```

### verify it's active
```powershell
git config --global core.hooksPath
# must return: C:/Users/pgwiz/.git-hooks
```

### rules for the hook
- ✅ hook lives at `$env:USERPROFILE\.git-hooks\commit-msg` — always, forever
- ✅ registered globally via `core.hooksPath` — one setup, all repos covered
- ❌ never override `core.hooksPath` in a local repo config
- ❌ never copy the hook into individual `.git/hooks/` folders — global only
- ❌ never delete or move the `$env:USERPROFILE\.git-hooks` folder

### linux/mac equivalent (for reference only)
```bash
mkdir -p ~/.git-hooks
cat > ~/.git-hooks/commit-msg << 'EOF'
#!/bin/sh
COMMIT_MSG_FILE=$1
BANNED="copilot\|anthropic\|claude\|openai\|github-actions"

if grep -qi "co-authored-by.*\($BANNED\)" "$COMMIT_MSG_FILE"; then
  echo ""
  echo "  [pgwiz] banned co-author detected."
  echo ""
  exit 1
fi
EOF
chmod +x ~/.git-hooks/commit-msg
git config --global core.hooksPath ~/.git-hooks
```

---

## 📌 global git identity — set once

```bash
git config --global user.name "pgwiz"
git config --global user.email "your@email.com"
```

---

> your commits. your name. your history. keep it clean.
