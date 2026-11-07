# Copilot Instructions

## Project Guidelines
- Pagination and keyword filtering for locations and cab listings should be delegated to the stored procedures; application code should pass CurrentPage, PageSize, and Keyword and not apply duplicate in-memory filtering or paging.