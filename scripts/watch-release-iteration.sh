#!/usr/bin/env bash
#
# watch-release-iteration.sh — should the parent agent open the next iteration round right now?
#
# The loop described in .agent_workspace/AUTO_ITERATION.md starts a round when `main` publishes a
# release. A GitHub CI subscription is meant to deliver that event, and when it does not deliver
# (deliveryCount=0 has happened) nothing wakes the parent up and the round starts late. This script is
# the fallback that does not depend on anything being delivered: the parent wakes on its own short
# timer, runs this once, and reads the exit code.
#
# It is READ-ONLY. It runs `gh release list`, `gh run list`, `git ls-remote` and GET-only `gh api`
# calls. It never commits, pushes, tags, opens or merges a pull request, or calls a paid API. The only
# thing it writes is one small state file outside the repository (see --state-file), which is how it
# tells "a release this loop has already reacted to" from "a release nobody has opened a round for".
#
# EXIT CODES — this is the contract. The parent only has to act on 10 and 20.
#
#     0   Nothing to do. Poll again later. Covers: no new release since the last round was opened,
#         CI still running, an iteration branch already in flight, no release at all yet.
#    10   OPEN THE NEXT ROUND NOW. A release newer than the one this script last reported is
#         published, the newest CI run on `main` succeeded, and no unmerged `cursor/*` branch is
#         still in flight.
#    20   `main` IS RED. The newest CI run on `main` did not succeed. The next round is a fix round;
#         no feature round opens on a red `main` (runbook §0).
#     2   The script could not answer — `gh` missing, not authenticated, or the API failed. This is
#         not "no": treat it as "look by hand".
#
# USAGE
#
#     scripts/watch-release-iteration.sh [options]
#
#     --json                  Emit one JSON object instead of the human-readable report.
#     --quiet                 Emit nothing; the exit code is the whole answer.
#     --peek                  Do not update the state file. Ask the same question again and get the
#                             same answer. Use this when you want to look without consuming the event.
#     --state-file PATH       Where the last-reported tag is remembered.
#                             Default: ${XDG_STATE_HOME:-$HOME/.local/state}/1remote-plus/release-watch.state
#     --repo OWNER/NAME       Override the repository. Default: whatever `gh` resolves here.
#     --branch NAME           The release branch to watch. Default: main.
#     --stale-hours N         A `cursor/*` branch whose newest commit is older than N hours is
#                             reported but no longer blocks a new round, so an abandoned branch cannot
#                             deadlock the loop. Default 12. 0 disables the exemption.
#     --seed                  Record the current release tag and exit 0 without deciding anything.
#                             Use once if you want the *next* release, not the current one, to fire.
#     -h, --help              This header.
#
# A first run with no state file reports 10 for whatever release is current. That is deliberate: a
# fresh parent with no memory should look at the newest release, and --seed is there for when it
# should not.
#
# EXAMPLE — the 90-second watch
#
#     scripts/watch-release-iteration.sh --json
#     case $? in
#       10) echo "start the next round" ;;
#       20) echo "main is red, start a fix round" ;;
#       0)  : ;;
#       *)  echo "look by hand" ;;
#     esac

set -uo pipefail

readonly EXIT_IDLE=0
readonly EXIT_UNKNOWN=2
readonly EXIT_START_ROUND=10
readonly EXIT_CI_RED=20

format=human
peek=false
seed=false
branch=main
repo=""
stale_hours=12
state_file="${XDG_STATE_HOME:-$HOME/.local/state}/1remote-plus/release-watch.state"

print_help() {
    # The header of this file is the documentation; there is no second copy of it to drift.
    sed -n '2,/^$/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
    case "$1" in
        --json)        format=json ;;
        --quiet)       format=quiet ;;
        --peek)        peek=true ;;
        --seed)        seed=true ;;
        --state-file)  state_file="${2:-}"; shift ;;
        --repo)        repo="${2:-}"; shift ;;
        --branch)      branch="${2:-}"; shift ;;
        --stale-hours) stale_hours="${2:-}"; shift ;;
        -h|--help)     print_help; exit 0 ;;
        *)             echo "unknown option: $1" >&2; exit "$EXIT_UNKNOWN" ;;
    esac
    shift
done

fail() {
    # Everything that cannot be answered lands here, so "could not answer" never gets mistaken for
    # "nothing to do" by a caller that only looks at the exit code.
    local reason="$1"
    case "$format" in
        json)  jq -n --arg reason "$reason" '{decision:"unknown", exit_code:2, reason:$reason}' ;;
        quiet) : ;;
        *)     echo "cannot answer: $reason" >&2 ;;
    esac
    exit "$EXIT_UNKNOWN"
}

command -v gh  >/dev/null 2>&1 || fail "the GitHub CLI (gh) is not on PATH"
command -v jq  >/dev/null 2>&1 || fail "jq is not on PATH"
command -v git >/dev/null 2>&1 || fail "git is not on PATH"

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || true)"
[ -n "$repo_root" ] || fail "not inside a git working copy"
cd "$repo_root" || fail "cannot enter $repo_root"

if [ -z "$repo" ]; then
    repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner 2>/dev/null || true)"
fi
[ -n "$repo" ] || fail "could not work out the repository; pass --repo OWNER/NAME"

now_epoch="$(date -u +%s)"

# Ages are printed because "the release is 40 seconds old" and "the release is nine hours old" call
# for very different reactions from a human reading the report over the parent's shoulder.
iso_to_epoch() {
    local iso="${1:-}"
    [ -n "$iso" ] && [ "$iso" != "null" ] || { echo ""; return; }
    date -u -d "$iso" +%s 2>/dev/null || echo ""
}

humanize_age() {
    local then="${1:-}"
    [ -n "$then" ] || { echo "unknown"; return; }
    local secs=$(( now_epoch - then ))
    [ "$secs" -lt 0 ] && secs=0
    if   [ "$secs" -lt 60 ];    then echo "${secs}s ago"
    elif [ "$secs" -lt 3600 ];  then echo "$(( secs / 60 ))m ago"
    elif [ "$secs" -lt 86400 ]; then echo "$(( secs / 3600 ))h $(( (secs % 3600) / 60 ))m ago"
    else echo "$(( secs / 86400 ))d ago"
    fi
}

# ---------------------------------------------------------------- the newest published release

release_json="$(gh release list --repo "$repo" --limit 20 \
                   --json tagName,publishedAt,isDraft,isPrerelease,isLatest 2>/dev/null)" \
    || fail "gh release list failed for $repo — not authenticated, or no such repository"

# --limit is not "the newest N published": drafts and pre-releases are in the same list, and a draft
# has no publishedAt at all. Filter first, then sort, rather than trusting the order gh returned.
release="$(printf '%s' "$release_json" | jq -c '
    [ .[] | select(.isDraft == false and .isPrerelease == false and .publishedAt != null) ]
    | sort_by(.publishedAt) | last // null')" || fail "could not parse the release list"

if [ "$release" = "null" ] || [ -z "$release" ]; then
    release_tag=""
    release_published=""
    release_url=""
else
    release_tag="$(printf '%s' "$release" | jq -r '.tagName')"
    release_published="$(printf '%s' "$release" | jq -r '.publishedAt')"
    # `gh release list --json` does not offer url. Ask the release itself rather than assembling a
    # github.com address by hand, which would be wrong on an Enterprise host. Cosmetic either way, so
    # a failure here must not take the answer down with it.
    release_url="$(gh release view --repo "$repo" "$release_tag" --json url --jq .url 2>/dev/null || true)"
fi
release_epoch="$(iso_to_epoch "$release_published")"

# ---------------------------------------------------------------- the newest CI run on the branch

run_json="$(gh run list --repo "$repo" --branch "$branch" --limit 20 \
               --json databaseId,workflowName,headSha,status,conclusion,createdAt,event,url 2>/dev/null)" \
    || fail "gh run list failed"

run="$(printf '%s' "$run_json" | jq -c 'sort_by(.createdAt) | last // null')" \
    || fail "could not parse the run list"

if [ "$run" = "null" ] || [ -z "$run" ]; then
    run_status="none"; run_conclusion=""; run_sha=""; run_created=""; run_url=""; run_workflow=""
else
    run_status="$(printf '%s'     "$run" | jq -r '.status')"
    run_conclusion="$(printf '%s' "$run" | jq -r '.conclusion // ""')"
    run_sha="$(printf '%s'        "$run" | jq -r '.headSha')"
    run_created="$(printf '%s'    "$run" | jq -r '.createdAt')"
    run_url="$(printf '%s'        "$run" | jq -r '.url')"
    run_workflow="$(printf '%s'   "$run" | jq -r '.workflowName')"
fi
run_epoch="$(iso_to_epoch "$run_created")"

# ---------------------------------------------------------------- iteration branches still in flight

# `git ls-remote` reads refs without fetching objects; the compare endpoint is a GET. A branch counts
# as in flight when it carries commits `main` does not have, which is the same thing the parent means
# by "unmerged" — a merged-and-not-deleted branch is 0 ahead and does not block.
branches_json='[]'
branch_names="$(git ls-remote --heads origin 'refs/heads/cursor/*' 2>/dev/null | awk '{print $2}' | sed 's#^refs/heads/##')"

if [ -n "$branch_names" ]; then
    while IFS= read -r b; do
        [ -n "$b" ] || continue
        cmp="$(gh api "repos/$repo/compare/$branch...$b" \
                 --jq '{ahead: .ahead_by, behind: .behind_by, last: (.commits | last | .commit.committer.date)}' \
                 2>/dev/null || true)"
        [ -n "$cmp" ] || continue
        ahead="$(printf '%s' "$cmp" | jq -r '.ahead // 0')"
        last="$(printf '%s'  "$cmp" | jq -r '.last // ""')"
        [ "$ahead" -gt 0 ] 2>/dev/null || continue

        last_epoch="$(iso_to_epoch "$last")"
        stale=false
        if [ "$stale_hours" -gt 0 ] 2>/dev/null && [ -n "$last_epoch" ]; then
            [ $(( now_epoch - last_epoch )) -gt $(( stale_hours * 3600 )) ] && stale=true
        fi

        branches_json="$(printf '%s' "$branches_json" | jq -c \
            --arg name "$b" --argjson ahead "$ahead" --arg last "$last" --argjson stale "$stale" \
            '. + [{name:$name, ahead:$ahead, last_commit:$last, stale:$stale}]')"
    done <<< "$branch_names"
fi

blocking_count="$(printf '%s' "$branches_json" | jq '[ .[] | select(.stale == false) ] | length')"

# ---------------------------------------------------------------- state

last_seen=""
[ -f "$state_file" ] && last_seen="$(head -n 1 "$state_file" 2>/dev/null | tr -d '[:space:]')"

remember() {
    local tag="$1"
    [ -n "$tag" ] || return 0
    mkdir -p "$(dirname "$state_file")" 2>/dev/null || return 0
    printf '%s\n' "$tag" > "$state_file" 2>/dev/null || return 0
}

if [ "$seed" = true ]; then
    remember "$release_tag"
    [ "$format" = quiet ] || echo "seeded: the next round fires on the first release after ${release_tag:-<none>}"
    exit "$EXIT_IDLE"
fi

# ---------------------------------------------------------------- the decision

decision=idle
exit_code="$EXIT_IDLE"
reason=""

if [ -z "$release_tag" ]; then
    reason="no published release yet"
elif [ "$run_status" = "none" ]; then
    reason="no CI run on $branch to judge"
elif [ "$run_status" != "completed" ]; then
    reason="CI on $branch is still $run_status ($run_workflow)"
elif [ "$run_conclusion" != "success" ]; then
    decision=ci_red
    exit_code="$EXIT_CI_RED"
    reason="the newest CI run on $branch concluded $run_conclusion"
elif [ "$blocking_count" -gt 0 ]; then
    reason="$blocking_count iteration branch(es) still ahead of $branch"
elif [ "$release_tag" = "$last_seen" ]; then
    reason="a round has already been opened for $release_tag"
else
    decision=start_round
    exit_code="$EXIT_START_ROUND"
    reason="$release_tag is published, CI is green, nothing in flight"
    [ "$peek" = true ] || remember "$release_tag"
fi

# ---------------------------------------------------------------- report

case "$format" in
    quiet) ;;
    json)
        jq -n \
            --arg repo "$repo" \
            --arg branch "$branch" \
            --arg decision "$decision" \
            --argjson exit_code "$exit_code" \
            --arg reason "$reason" \
            --arg release_tag "$release_tag" \
            --arg release_published "$release_published" \
            --arg release_url "$release_url" \
            --arg release_age "$(humanize_age "$release_epoch")" \
            --arg ci_status "$run_status" \
            --arg ci_conclusion "$run_conclusion" \
            --arg ci_workflow "$run_workflow" \
            --arg ci_sha "$run_sha" \
            --arg ci_created "$run_created" \
            --arg ci_url "$run_url" \
            --arg last_seen "$last_seen" \
            --argjson branches "$branches_json" \
            '{repo:$repo, branch:$branch, decision:$decision, exit_code:$exit_code, reason:$reason,
              release:{tag:$release_tag, published_at:$release_published, age:$release_age, url:$release_url},
              ci:{status:$ci_status, conclusion:$ci_conclusion, workflow:$ci_workflow,
                  head_sha:$ci_sha, created_at:$ci_created, url:$ci_url},
              iteration_branches:$branches,
              last_round_opened_for:$last_seen}'
        ;;
    *)
        echo "release watch — $repo ($branch) — $(date -u +%Y-%m-%dT%H:%M:%SZ)"
        echo
        if [ -n "$release_tag" ]; then
            printf 'release   %-12s published %s (%s)\n' "$release_tag" "$release_published" "$(humanize_age "$release_epoch")"
            printf '          %s\n' "$release_url"
        else
            echo "release   none published"
        fi
        if [ "$run_status" = none ]; then
            echo "ci        no run on $branch"
        else
            printf 'ci        %-12s %s  %s  %s (%s)\n' \
                "${run_conclusion:-$run_status}" "$run_status" "${run_sha:0:8}" "$run_workflow" "$(humanize_age "$run_epoch")"
            printf '          %s\n' "$run_url"
        fi
        branch_total="$(printf '%s' "$branches_json" | jq 'length')"
        if [ "$branch_total" -eq 0 ]; then
            echo "branches  no cursor/* branch ahead of $branch"
        else
            printf 'branches  %s ahead of %s, %s blocking\n' "$branch_total" "$branch" "$blocking_count"
            printf '%s' "$branches_json" | jq -r '.[] | "            \(.name)  +\(.ahead)  last \(.last_commit)\(if .stale then "  [stale, not blocking]" else "" end)"'
        fi
        printf 'state     last round opened for %s\n' "${last_seen:-<nothing>}"
        echo
        printf 'decision  %s (%s) — %s\n' "$exit_code" "$decision" "$reason"
        ;;
esac

exit "$exit_code"
