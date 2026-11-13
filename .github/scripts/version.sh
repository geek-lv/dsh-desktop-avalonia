#!/usr/bin/env bash
# Work out the version for this CI run. Git tags (vX.Y.Z) are the only source of truth;
# DshDesktop.csproj <Version> is just the local-build default and the seed before the first tag.
#
#   tag push vX.Y.Z           -> X.Y.Z                 release
#   dispatch, bump=patch      -> last tag + 1 patch    release (tag created by the release job)
#   dispatch, bump=minor|major-> likewise              release
#   anything else             -> <last tag>.<run>      CI build only, e.g. 2.0.0.37 (MSI needs digits only)
#   no v* tag yet             -> "last tag" = csproj <Version>; the first bumped release is that version itself
#
# Inputs (env): GITHUB_REF, GITHUB_REF_NAME, GITHUB_RUN_NUMBER, BUMP (none|patch|minor|major)
# Outputs: version=..., release=true|false, tag=vX.Y.Z (only when release) to $GITHUB_OUTPUT (or stdout)
set -euo pipefail

out="${GITHUB_OUTPUT:-/dev/stdout}"
bump="${BUMP:-none}"
run="${GITHUB_RUN_NUMBER:-0}"
semver='^[0-9]+\.[0-9]+\.[0-9]+$'

csproj="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' DshDesktop.csproj | head -1)"
last_tag="$(git tag --list 'v[0-9]*' --sort=-v:refname | grep -E "^v[0-9]+\.[0-9]+\.[0-9]+$" | head -1 || true)"
last="${last_tag#v}"

release=false
if [[ "${GITHUB_REF:-}" == refs/tags/v* ]]; then
  version="${GITHUB_REF_NAME#v}"
  [[ "$version" =~ $semver ]] || { echo "::error::tag $GITHUB_REF_NAME is not vX.Y.Z" >&2; exit 1; }
  release=true
elif [[ "$bump" != none ]]; then
  if [[ -z "$last" ]]; then
    version="$csproj"                       # first release ever: the seed version as-is
  else
    IFS=. read -r ma mi pa <<< "$last"
    case "$bump" in
      patch) version="$ma.$mi.$((pa + 1))" ;;
      minor) version="$ma.$((mi + 1)).0" ;;
      major) version="$((ma + 1)).0.0" ;;
      *) echo "::error::bump must be none, patch, minor or major (got '$bump')" >&2; exit 1 ;;
    esac
  fi
  [[ "$version" =~ $semver ]] || { echo "::error::computed version '$version' is not X.Y.Z" >&2; exit 1; }
  if git rev-parse -q --verify "refs/tags/v$version" >/dev/null; then
    echo "::error::tag v$version already exists" >&2; exit 1
  fi
  release=true
else
  version="${last:-$csproj}.$run"
fi

{
  echo "version=$version"
  echo "release=$release"
  if [[ "$release" == true ]]; then echo "tag=v$version"; fi
} >> "$out"
echo "version $version (release=$release, last tag ${last_tag:-none}, csproj $csproj, bump $bump)" >&2
