#!/usr/bin/env bash
# Build a GitHub release body from git log between the previous tag and HEAD.
# changelogithub only understands conventional commits, so a typical 1Remote
# tag (plain English subjects) used to publish as "No significant changes".
set -euo pipefail

TAG="${1:-${GITHUB_REF_NAME:-}}"
REPO="${GITHUB_REPOSITORY:-}"

if [ -z "$TAG" ]; then
  echo "usage: release-notes.sh <tag>" >&2
  exit 1
fi

PREV="$(git describe --tags --abbrev=0 "${TAG}^" 2>/dev/null || true)"
if [ -z "$PREV" ]; then
  PREV="$(git tag --merged "$TAG" --sort=-creatordate |
    awk -v tag="$TAG" '$0 != tag && !found { previous=$0; found=1 }
      END { if (found) print previous }' || true)"
fi

echo "## What's Changed"
echo

# CI writes the version bump back to main as "chore(release): <tag> [skip ci]", one per release. Those
# commits say nothing about what changed, so they are left out of the list.
LOG_OPTS=(-n 100 --pretty=format:'- %s (%h)' --no-merges --invert-grep --grep='^chore(release): ')

if [ -n "$PREV" ]; then
  LOG="$(git log "${LOG_OPTS[@]}" "${PREV}..${TAG}")"
else
  LOG="$(git log "${LOG_OPTS[@]}" "$TAG")"
fi

if [ -z "$LOG" ]; then
  echo "- ${TAG}"
else
  printf '%s\n' "$LOG"
fi

echo

if [ -n "$PREV" ] && [ -n "$REPO" ]; then
  echo "**Full Changelog**: https://github.com/${REPO}/compare/${PREV}...${TAG}"
elif [ -n "$REPO" ]; then
  echo "https://github.com/${REPO}/releases/tag/${TAG}"
fi
