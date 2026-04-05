# FERMM Documentation Index

Welcome to the FERMM documentation! Here you'll find comprehensive guides for setup, deployment, development, and troubleshooting.

---

## 🚀 Quick Start

**New here?** Start with the root-level files:
1. **README.md** — Project overview and features
2. **setup.md** — 5-minute first-run setup guide

Then explore this folder for detailed information.

---

## 📋 Documentation Files

### Deployment & Operations

| File | Purpose |
|------|---------|
| **UBUNTU-DEPLOYMENT-GUIDE.md** | Step-by-step Ubuntu server setup |
| **DOCKER-IMAGE-CONTENTS.md** | What's inside the Docker image |
| **DEPLOYMENT-CHECKLIST.md** | Testing checklist before production |

### Phase 6: Overlay Feature

| File | Purpose |
|------|---------|
| **PHASE-6-OVERLAY-COMPLETE.md** | Complete technical architecture (20KB) |
| **PHASE-6-DEPLOYMENT-GUIDE.md** | Build, test, and deploy overlay locally |
| **PHASE-6-DELIVERY-PACKAGE.md** | Full delivery summary and instructions |
| **PHASE-6-FINAL-COMPLETION.md** | Completion report with metrics |
| **PHASE-6-INDEX.md** | Quick reference guide |

### Project Planning

| File | Purpose |
|------|---------|
| **FERMM-PLAN.md** | Overall project plan and architecture |
| **GIT-PUSH-SUMMARY.md** | GitHub repository push summary |

### Previous Phases

| File | Purpose |
|------|---------|
| **PHASE-3-COMPLETE.md** | Keylogger implementation |
| **PHASE-4-KEYLOGGER-COMPLETE.md** | Keylogger enhancements |
| **PHASE-5-AUTO-DISCOVERY-COMPLETE.md** | Auto-discovery features |

---

## 🎯 By Use Case

### I want to...

**...deploy the server on Ubuntu**
→ Read: `UBUNTU-DEPLOYMENT-GUIDE.md`

**...understand what's in the Docker image**
→ Read: `DOCKER-IMAGE-CONTENTS.md`

**...test everything before production**
→ Read: `DEPLOYMENT-CHECKLIST.md`

**...understand the overlay feature**
→ Read: `PHASE-6-OVERLAY-COMPLETE.md`

**...build the image locally**
→ Read: `PHASE-6-DEPLOYMENT-GUIDE.md`

**...see what was delivered**
→ Read: `PHASE-6-DELIVERY-PACKAGE.md`

**...understand the overall architecture**
→ Read: `FERMM-PLAN.md`

---

## 🔍 Technical Details

### Overlay Architecture (Phase 6)
- Non-recordable top-level Windows overlay
- Real-time message sync between dashboard and overlay
- Named Pipe IPC for agent ↔ overlay communication
- WebSocket relay for dashboard ↔ agent messaging
- Hotkey support (Ctrl+Shift+Space)

**See**: `PHASE-6-OVERLAY-COMPLETE.md`

### Docker Setup
- Multi-stage Alpine build (optimized for size)
- PostgreSQL 16 for persistence
- Nginx reverse proxy (optional HTTPS)
- Auto-health checks

**See**: `DOCKER-IMAGE-CONTENTS.md`

### Security Features
- JWT authentication
- Named encryption for agent config
- Database separation
- Credentials in `.env` (not committed)

**See**: `UBUNTU-DEPLOYMENT-GUIDE.md`

---

## 📦 Component Files

### Agent (C# .NET 8)
- `fermm-agent/` in root directory
- 30+ source files
- Overlay service + hotkey handler
- WebSocket client
- Named Pipe IPC

### Server (Python FastAPI)
- `fermm-server/` in root directory
- 20+ source files
- Overlay endpoints + WebSocket relay
- PostgreSQL integration
- Device management

### Dashboard (React + TypeScript)
- `fermm-dashboard/` in root directory
- 20+ source files
- Overlay panel component
- Real-time message display
- Device control interface

### Docker & Infrastructure
- `docker-compose.yml` — Standard compose file
- `docker-compose.ubuntu.yml` — Ubuntu-optimized
- `Dockerfile` — Multi-stage build
- `nginx.conf` — Reverse proxy config
- `scripts/` — Build and deployment scripts

---

## 🚀 Quick Commands

```bash
# Start everything
docker-compose up -d

# View logs
docker-compose logs -f

# Check status
docker-compose ps

# Stop services
docker-compose stop

# Update image
docker-compose pull && docker-compose up -d
```

---

## 📚 File Statistics

| Category | Count | Total |
|----------|-------|-------|
| Documentation (this folder) | 13 files | ~150 KB |
| Phase documentation | 5 files | ~55 KB |
| Deployment guides | 3 files | ~32 KB |
| Planning & reference | 2 files | ~55 KB |

---

## 🔗 Related Files

**In root directory:**
- `README.md` — Project overview
- `setup.md` — First-run setup (5 minutes)
- `docker-compose.yml` — Standard Docker Compose
- `docker-compose.ubuntu.yml` — Ubuntu-optimized
- `.env.example` — Example environment variables

**In subdirectories:**
- `fermm-agent/` — Agent source code
- `fermm-server/` — Server source code
- `fermm-dashboard/` — Dashboard source code
- `scripts/` — Build and deploy scripts

---

## ⏱️ Reading Time Guide

- Quick overview: 5 minutes (README.md + setup.md)
- First deployment: 10 minutes (setup.md + UBUNTU-DEPLOYMENT-GUIDE.md)
- Full understanding: 30 minutes (PHASE-6-OVERLAY-COMPLETE.md + DEPLOYMENT-CHECKLIST.md)
- Deep technical dive: 1 hour (PHASE-6-OVERLAY-COMPLETE.md + FERMM-PLAN.md)

---

## 🆘 Troubleshooting

**Can't find what you need?**

1. Search for keywords in filenames above
2. Check `DEPLOYMENT-CHECKLIST.md` for common issues
3. Review `UBUNTU-DEPLOYMENT-GUIDE.md` for setup problems
4. See `DOCKER-IMAGE-CONTENTS.md` for Docker questions

---

## 📝 Document Organization

```
md/ (this folder)
├── UBUNTU-DEPLOYMENT-GUIDE.md     ← Read first if deploying
├── DOCKER-IMAGE-CONTENTS.md        ← Read for image details
├── DEPLOYMENT-CHECKLIST.md         ← Read before production
├── PHASE-6-OVERLAY-COMPLETE.md     ← Technical reference
├── PHASE-6-DEPLOYMENT-GUIDE.md     ← Build instructions
├── PHASE-6-DELIVERY-PACKAGE.md     ← Full summary
├── PHASE-6-FINAL-COMPLETION.md     ← Metrics & status
├── PHASE-6-INDEX.md                ← Quick reference
├── FERMM-PLAN.md                   ← Project plan
├── GIT-PUSH-SUMMARY.md             ← Repository info
├── PHASE-3-COMPLETE.md             ← Previous work
├── PHASE-4-KEYLOGGER-COMPLETE.md   ← Previous work
└── PHASE-5-AUTO-DISCOVERY-COMPLETE.md ← Previous work
```

---

## ✅ Checklist: What to Read

- [ ] README.md (root) — What is FERMM?
- [ ] setup.md (root) — How do I start?
- [ ] UBUNTU-DEPLOYMENT-GUIDE.md (this folder) — Deploy on server
- [ ] DOCKER-IMAGE-CONTENTS.md (this folder) — Understand the image
- [ ] DEPLOYMENT-CHECKLIST.md (this folder) — Test before production
- [ ] PHASE-6-OVERLAY-COMPLETE.md (this folder) — Understand overlay

---

**Status**: Complete documentation set  
**Last updated**: April 5, 2026  
**Version**: Phase 6 Complete

---

*For questions or issues, check the relevant guide above. All common problems are covered in DEPLOYMENT-CHECKLIST.md.*
