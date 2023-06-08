# SCIM

## Customizations done by LZZ

1. Removed RootController to prevent multiple controllers from being matched
2. Updated all routes to remove "scim" prefix (because we are hosting a standalone API, if in the future we want to use SCIM in an existing API, then we can revert this)