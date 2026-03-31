#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./loop.sh                # build mode, unlimited iterations
  ./loop.sh 20             # build mode, max 20 iterations
  ./loop.sh build 20       # build mode, max 20 iterations
  ./loop.sh plan           # plan mode, unlimited iterations
  ./loop.sh plan 5         # plan mode, max 5 iterations
EOF
}

is_integer() {
  [[ "${1:-}" =~ ^[0-9]+$ ]]
}

MODE="build"
PROMPT_FILE="PROMPT_build.md"
MAX_ITERATIONS=0

case "${1:-}" in
  "")
    ;;
  plan)
    MODE="plan"
    PROMPT_FILE="PROMPT_plan.md"
    if [[ $# -ge 2 ]]; then
      is_integer "$2" || { echo "Error: max iterations must be a non-negative integer."; usage; exit 1; }
      MAX_ITERATIONS="$2"
    fi
    ;;
  build)
    MODE="build"
    PROMPT_FILE="PROMPT_build.md"
    if [[ $# -ge 2 ]]; then
      is_integer "$2" || { echo "Error: max iterations must be a non-negative integer."; usage; exit 1; }
      MAX_ITERATIONS="$2"
    fi
    ;;
  -h|--help|help)
    usage
    exit 0
    ;;
  *)
    if is_integer "$1"; then
      MAX_ITERATIONS="$1"
    else
      echo "Error: invalid argument '$1'."
      usage
      exit 1
    fi
    ;;
esac

command -v git >/dev/null 2>&1 || { echo "Error: git is required."; exit 1; }
command -v claude >/dev/null 2>&1 || { echo "Error: claude CLI is required."; exit 1; }

if [[ ! -f "$PROMPT_FILE" ]]; then
  echo "Error: prompt file '$PROMPT_FILE' not found."
  exit 1
fi

CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
if [[ -z "$CURRENT_BRANCH" || "$CURRENT_BRANCH" == "HEAD" ]]; then
  echo "Warning: no active git branch detected. Push steps will be skipped."
fi

ITERATION=0

echo "========================================"
echo "Mode:     $MODE"
echo "Prompt:   $PROMPT_FILE"
echo "Branch:   ${CURRENT_BRANCH:-none}"
if [[ "$MAX_ITERATIONS" -gt 0 ]]; then
  echo "Max iters:$MAX_ITERATIONS"
else
  echo "Max iters:unlimited"
fi
echo "========================================"

while true; do
  if [[ "$MAX_ITERATIONS" -gt 0 && "$ITERATION" -ge "$MAX_ITERATIONS" ]]; then
    echo "Reached max iterations: $MAX_ITERATIONS"
    break
  fi

  echo ""
  echo "---- Ralph iteration $((ITERATION + 1)) ----"

  cat "$PROMPT_FILE" | claude -p \
    --dangerously-skip-permissions \
    --output-format=stream-json \
    --model opus \
    --verbose

  if [[ -n "$CURRENT_BRANCH" && "$CURRENT_BRANCH" != "HEAD" ]]; then
    git push origin "$CURRENT_BRANCH" || {
      echo "Push failed. Attempting upstream setup..."
      git push -u origin "$CURRENT_BRANCH" || echo "Warning: push did not succeed."
    }
  fi

  ITERATION=$((ITERATION + 1))

  # Auto-stop: exit when no uncompleted items remain in the plan
  if [[ -f "IMPLEMENTATION_PLAN.md" ]]; then
    REMAINING=$(grep -c '^\- \[ \]' IMPLEMENTATION_PLAN.md 2>/dev/null || true)
    if [[ "$REMAINING" -eq 0 ]]; then
      echo ""
      echo "========================================"
      echo "All items complete. Loop finished after $ITERATION iteration(s)."
      echo "========================================"
      break
    else
      echo ">> $REMAINING uncompleted item(s) remaining."
    fi
  fi
done
