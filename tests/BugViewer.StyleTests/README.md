# Style integration fixture

Run `dotnet run --project tests/BugViewer.StyleTests --urls http://127.0.0.1:5267`
from the BugViewer repository, then `node tests/style-isolation-browser.test.mjs`.
The browser test requires Playwright and uses headless Edge on Windows. If it is
installed outside this repository, set `PLAYWRIGHT_MODULE_PATH` to the absolute
path of its `playwright-core` package. `BUGVIEWER_URL` can select another port.

The fixture loads the real Blazor and Fluent UI components and their generated
stylesheets. The test enables conflicting host CSS after capturing defaults,
checks computed styles, changes the documented theme variables, exercises step
editing and Save As, and checks a narrow layout. Screenshots go to a temporary
directory, or `BUGVIEWER_SCREENSHOTS` when set. No GPU rendering is stubbed.
