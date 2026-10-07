# REST API

All CRM endpoints require the ASP.NET Identity application cookie. Write requests also require an anti-forgery token because cookies are automatically sent by browsers. There is no JWT/bearer-token implementation.

## Session setup

1. `GET /api/auth/csrf` stores an anti-forgery cookie and returns `{ "requestToken": "..." }`.
2. Retain cookies and send `POST /api/auth/login` with `{ "email": "sales@acxiom.test", "password": "Acxiom@123" }`. Put the request token in `X-CSRF-TOKEN`.
3. Fetch `/api/auth/csrf` again after login: the new request token is bound to the signed-in identity.
4. Use that token for POST/PUT/DELETE requests and retain the session cookies.
5. `POST /api/auth/logout` terminates the session.

The anti-forgery endpoint deliberately returns the request token needed by the client. CRM DTOs never expose authentication tokens, password hashes or internal Identity fields.

## Resources

For each of `customers`, `leads`, `opportunities`, `followups`, `activities`:

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/{resource}` | Paginated visible records |
| GET | `/api/{resource}/{id}` | One visible DTO |
| POST | `/api/{resource}` | Create; 201 with Location header |
| PUT | `/api/{resource}/{id}` | Validate and update; 200 |
| DELETE | `/api/{resource}/{id}` | Soft delete if dependencies allow; 200 |

`POST /api/{followups|activities}/{id}/complete` marks a planned follow-up or pending activity as Completed and returns the updated DTO (409 if it is not open).

`POST /api/leads/{id}/convert` converts a qualified lead and returns `{ "customerId": "guid" }`.

`GET /api/reports/pipeline` returns stage totals and open-pipeline owner totals including weighted amounts. SalesExecutive receives only owned records.

## List query

`q`, `status`, `owner`, `type`, `stage`, `from`, `to`, `sort`, `page`. Dates are `yyyy-MM-dd`. Sort is `newest`, `oldest`, `name`, `amount` or `date`. Page size is 10, and page numbers are clamped to the available range. `from`/`to` use activity/follow-up dates for those resources and creation dates for the others.

Example response:

```json
{
  "items": [{ "id": "guid", "module": "customers", "name": "Ananya Desai", "status": "Active", "ownerId": "guid", "owner": "Rahul Verma" }],
  "total": 1,
  "page": 1,
  "size": 10
}
```

DTOs also include applicable contact details, timestamps, code, company/address, notes, related IDs, source/priority, values, stage/probability and dates. Inapplicable fields are null or zero. This shared DTO keeps the assignment's API small; it does not serialise EF navigations.

## Customer body

```json
{
  "name": "Amit Kumar",
  "email": "amit@example.test",
  "phone": "9876543299",
  "company": "Amit Enterprises",
  "address": "MG Road",
  "city": "Pune",
  "state": "Maharashtra",
  "status": "Active",
  "ownerId": "actual-user-id",
  "notes": "Interested in an annual service plan."
}
```

For leads, add `source`, `priority` and `expectedValue`; initial status must be `New`. For opportunities, provide `customerId`, optional `leadId`, `amount`, `probability`, `closeDate`, `stage`, `status`, `source` and `ownerId`. For follow-ups/activities, provide `name` as subject, `dueDate`, `type`, `status`, `ownerId`, `notes` and exactly one related record ID. Follow-ups additionally support `opportunityId`.

PUT submits the full form body, not a partial patch. The route ID is authoritative. Server-generated fields such as timestamps, codes, creator and deletion state are not writable through the form model.

## Errors

- 400: malformed/invalid fields, invalid business rule, or missing/invalid anti-forgery token.
- 401: missing/expired login, invalid login, inactive or locked account.
- 403: role restriction or unauthorised assignment.
- 404: missing record or record outside the user's scope.
- 409: duplicate contact, related-record conflict, or invalid repeat conversion.
- 429: authentication rate limit.
- 500: unexpected failure with a generic safe message.

Business errors use `{ "error": "message", "errors": { "Field": ["message"] } }`. Annotation/model-binding errors use the framework's ValidationProblemDetails with an `errors` dictionary. Anti-forgery failures use the framework's 400 response. Clients should check HTTP status before parsing a response body.

## Report CSV

`GET /Reports?report=customers&export=true` exports the full filtered report, not just the visible page. The same role checks apply. Available report names: customers, leads, followups, opportunities, pipeline, conversion, user-activity and audit. Audit is Admin only; user activity is Admin/Manager only.
