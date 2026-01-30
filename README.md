# Deploy to ustadyazilim/UstadDesktop (production repo)

This repo is the **UstadDesktop** codebase. The canonical production repo is:

- **https://github.com/ustadyazilim/UstadDesktop.git**

The fork **ustadyazilim/yesiLdefterV3** (and **tekinucar/yesiLdefterV3**) are no longer the push targets for this project.

## Git configuration (this repo)

- **Push target:** Only **ustadyazilim/UstadDesktop** is allowed. Pushing to `origin` (yesiLdefterV3) or `upstream` (tekinucar/yesiLdefterV3) is **blocked** by the `.git/hooks/pre-push` hook.
- **Default push:** `branch.master.remote` is set to **ustadyazilim**, and `remote.pushDefault` is **ustadyazilim**, so a plain `git push` from `master` goes to UstadDesktop.
- **Remotes:**
  - `ustadyazilim` → https://github.com/ustadyazilim/UstadDesktop.git (use this for push)
  - `origin` → https://github.com/ustadyazilim/yesiLdefterV3.git (pull-only; push blocked)
  - `upstream` → https://github.com/tekinucar/yesiLdefterV3 (pull-only; push blocked)

## Pushing to UstadDesktop

From this directory (clean, no build folders are committed; `.gitignore` excludes `bin/`, `obj/`, etc.):

```bash
# Push current branch to UstadDesktop (recommended)
git push ustadyazilim

# Or push master explicitly
git push ustadyazilim master

# First-time: set upstream so future "git push" uses ustadyazilim
git push -u ustadyazilim master
```

If you run `git push origin` or `git push upstream`, the pre-push hook will block it and remind you to use `git push ustadyazilim`.

## Environment (Development vs Production)

The Windows Forms app uses a single **Environment** setting (Development / Production) stored in the registry and shared by:

- **Ustad API** (auth, firms, core): Development = `http://localhost:5001`, Production = `http://143.198.228.153:8080`
- **WhatsApp API**: Development = `http://localhost:8080`, Production = `http://143.198.228.153:8080/api`

Users can switch via **API Ortamı** in the WhatsApp form (Development / Production). The same environment is used for bulk WhatsApp notifications and for main API calls.

## Clean clone for a new machine

To get a clean copy without build artifacts:

```bash
git clone https://github.com/ustadyazilim/UstadDesktop.git
cd UstadDesktop
# Build folders (bin/obj) are not in the repo; build in Visual Studio or msbuild
```
