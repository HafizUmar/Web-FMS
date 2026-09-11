# CrockeryFactory.Client

The Angular front end for the Crockery Factory Management System.

This is an **npm project, not a .NET one**, so it deliberately has no `.csproj` and does
not appear in `CrockeryFactory.sln`. Visual Studio's Solution Explorer lists solution
projects only; to see these files use **Solution Explorer → Switch Views → Folder View**,
or open `src/CrockeryFactory.Client` in VS Code.

Angular 21, standalone components, signals, zoneless change detection, Angular Material 21.

## Where the build goes

`ng build` writes **into `../CrockeryFactory.Web/wwwroot`**, not into `dist/`. That is set
in `angular.json` (`outputPath`), and it matters: the API authenticates with an HttpOnly,
`SameSite=Strict` cookie, which the browser only sends on a same-origin request. The
front end therefore has to be served from the same origin as the API rather than from a
separate static host.

`wwwroot` is git-ignored, so a fresh clone has no front end on disk until you build one.
That is the usual reason the application starts and serves nothing but the API.

## First run

```bash
cd src/CrockeryFactory.Client
npm install
npm run build
```

Then start `CrockeryFactory.Web` (F5 in Visual Studio, or `dotnet run --project
src/CrockeryFactory.Web`) and open its URL. `Program.cs` rewrites any non-`/api`,
non-`/swagger` GET without a file extension to `index.html`, so client-side routes such as
`/dispatches` survive a page refresh.

`npm install` uses the `legacy-peer-deps=true` in `.npmrc`; it is there because npm 10's
dependency resolver crashes on this tree otherwise, and removing it will break a clean
install.

## Working on the front end

```bash
npm start          # ng serve on http://localhost:4200
```

`proxy.conf.json` forwards `/api` and `/swagger` from `:4200` to the API's **HTTPS**
endpoint on `:7150`, which keeps the session cookie same-origin during development. Start
the API first, or every request fails with a connection error rather than an
authentication one.

It has to be the HTTPS endpoint. The API calls `UseHttpsRedirection`, so proxying to
`http://localhost:5150` gets a 307 to `https://localhost:7150`; the browser treats that as
a cross-origin hop from `:4200`, the request fails as a network error, and the login page
reports "Cannot reach the server" while the API is in fact running normally. The session
cookie is `CookieSecurePolicy.Always` as well, so it would not be stored off a plain-http
response in any case.

For a production-shaped build that refreshes `wwwroot` as you edit:

```bash
npm run watch
```

## Signing in

The development seeder creates three accounts, all with the password `Factory!Pass99`:

| Username | Role | Sees |
|---|---|---|
| `owner` | Owner | Everything, including prices and reports |
| `clerk` | Clerk | Production, dispatches and payments |
| `admin` | Administrator | Users, settings and the audit log |

The navigation and the route guards are both driven by the `permissions` array the server
returns from `/api/v1/auth/me`, so signing in as the clerk genuinely hides the
administration pages rather than only disabling them.

## Layout

```
src/app/
  core/        API types, services, interceptors, guards, formatting, theme
  layout/      the shell: top bar, navigation rail
  shared/      error banner, confirm dialog, warning list
  features/    one folder per screen group
```

`src/styles.scss` holds the theme: every surface, line and status colour is a custom
property with a light and a dark value, which is what lets the dark theme be a token swap
rather than a second stylesheet. Components should use those tokens rather than literal
colours.

## The API contract

`docs/frontend-pdr.md` in the repository root documents every endpoint with request and
response examples captured from the running API.
