## v1.7.14 (patch)

Changes since v1.7.13:

- Merge main into fix/142-fuzzy-inner-whitespace (Claude)
- Ignore whitespace inside a fuzzy filter, not just at its ends [patch] (Claude)
- Stream glob and regex Filter results instead of sorting by an unset score [patch] (Claude)
- Split class bracket scanning out of ExpandGlobClasses to reduce its cognitive complexity (Claude)
- Match every member of a glob class that mixes a range with other characters [patch] (Claude)
- Match a trailing dash in a glob class literally [patch] (Claude)

