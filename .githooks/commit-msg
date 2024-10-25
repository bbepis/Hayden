#!/bin/bash

# Regex to validate the type pattern
REGEX="^((Merge[ a-z-]* branch.*)|(Revert*)|((build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\(.*\))?!?: .*))"

FILE=`cat $1` # File containing the commit message

echo "Commit Message: ${FILE}"

if ! [[ $FILE =~ $REGEX ]]; then
	echo >&2 "ERROR: Commit aborted for not following the Conventional Commit standard.​"
	exit 1
else
	echo >&2 "Valid commit message."
fi