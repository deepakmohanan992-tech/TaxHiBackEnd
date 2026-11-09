# Copilot Instructions

## Project Guidelines
- Pagination and keyword filtering for locations and cab listings should be delegated to the stored procedures; application code should pass CurrentPage, PageSize, and Keyword and not apply duplicate in-memory filtering or paging.
- Use the existing CommonResponse<T> envelope consistently for API success and failure responses, including authentication endpoints; do not return raw DTOs or unwrapped response objects.