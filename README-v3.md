# Amazon delivery finder: 10-worker update

## Launch

Pause and close the older app normally, then double-click `Start.cmd`. It now launches `AmazonPincodeChecker-v3.exe`. Keep the DLLs and `src/page.js` beside it. Click Resume to continue the last saved run, or load `pincode.txt` and start a new run. Resume keeps saved product/price settings but uses the currently selected delay.

## Four laptops

Extract `AmazonPincodeChecker-10-workers.zip` to a separate local folder on each Windows laptop. Microsoft Edge WebView2 Runtime is required. Double-click `Start.cmd`.

On the first run, load the assigned file and start a **new check**:

| Laptop | Input file | Pincodes |
| --- | --- | --- |
| 1 | pincode_1.txt | 4,897 |
| 2 | pincode_2.txt | 4,897 |
| 3 | pincode_3.txt | 4,896 |
| 4 | pincode_4.txt | 4,896 |

The four files cover all 19,586 entries in the original `pincode.txt` with no duplicates or overlaps. Each laptop runs 10 workers with a minimum 3-second delay. Keep each laptop's results and checkpoints in its own local folder. Use Resume only after that laptop has started its assigned list; resuming an older copied checkpoint would use the older list. Collect `good_pincodes.txt` separately from each laptop when finished.

## Behavior

- 10 dedicated browser threads, with separate cookie stores under `%LOCALAPPDATA%\AmazonPincodeChecker\Worker1` through `Worker10`.
- Workers start hidden and do not use the mouse, keyboard or foreground application. You can minimize the main app and use your computer. Keep the application running and the computer awake.
- Minimum and default wait: **3 seconds per worker**, between checks and between retries. Loading and verification take additional time.
- Page-load failures and unstable pages get **three attempts total**, with the pincode reserved to the same worker throughout. Exhausted attempts are logged as RETRY_LATER; the worker moves on. Resume retries unfinished pincodes.
- Unclear results are logged for review and remain unfinished. A CAPTCHA parks only that worker; other workers continue. Select its number and click **Show selected worker**, resolve it yourself, then Resume after pausing or finishing the current run.
- Closing a worker window hides it; it keeps working. Pause stops all workers. Pause-after-first-match is now **off by default**. Selecting it intentionally stops all workers on a match.
- A browser startup failure does not stop peers; the failed worker is recreated on Resume. Output-storage failures stop the whole run to protect results.

## Concurrency and idempotency

The synchronized queue grants each entry to one worker and retains the lease through retries. Input pincodes are deduplicated. Completed and deferred entries cannot be claimed again during the same run. Resume schedules only unfinished work. Session mutations and file writes are serialized on the main GUI thread.

Accepted results are atomically committed to `results/<run>/committed/<index>.json` before updating the checkpoint. Repeating a commit cannot duplicate good_pincodes. Resume replays committed records to recover from a crash between the commit and checkpoint. A check interrupted before a durable commit can be repeated intentionally; Amazon requests do not have an exactly-once guarantee.

A named process mutex prevents simultaneous updated-app instances. The update also refuses startup while the old executable is running. Do not launch an older version alongside it: old versions cannot participate in the new coordination protocol. Starting a new run intentionally rechecks its list because stock and price change.

## Output

- `good_pincodes.txt`: confirmed matching pincodes, one per line, without duplicates.
- `results/<run>/good_pincodes.txt`: per-run copy.
- `results/<run>/unfinished_pincodes.txt`: unfinished codes, including active and deferred work.
- `results/<run>/checkpoint.json`: settings, progress and the `good_pincodes` variable.
- `results/<run>/committed/`: durable canonical results.
- `results/<run>/checks.csv`: observation log, including REVIEW and RETRY_LATER entries. Repeated observations can appear after recovery; canonical results and good_pincodes remain deduplicated.

Default price filter: ₹3,119 plus 5%, including identified delivery charges. The app verifies the exact pincode, ASIN, delivery indication, enabled Add to Cart and displayed price. It does not create a full street address or place an order. Coupons, bank offers, optional installation and checkout-only charges are excluded. English page text is required.

## CPU and GPU

Browser work runs concurrently on CPU threads. WebView2 hardware-accelerated rendering remains enabled, subject to runtime, GPU and driver support. Background timer and renderer throttling switches are supplied. Delivery eligibility is answered by Amazon's servers; it cannot be processed as GPU computation. The app does not force 100% CPU/GPU usage or guarantee that 10 workers will be faster on every machine.

## Validation

The update compiles. All 12 existing offline page-adapter tests pass. New 25-thread concurrency tests compile, but Windows security blocked their execution with a virus-or-potentially-unwanted-software message; the cause has not been established. The concurrency stress test remains unverified. The current 10-worker build compiles and all 12 offline page-adapter tests pass; live browser execution was not retested for this update.

Source: `src/App.cs`, `src/BrowserWorker.cs`, `src/WorkQueue.cs`. Build: `build.ps1`. Queue tests: `src/QueueTests.cs`. Page tests: `node src/test-page.cjs`. C# browser fixture tests: `AmazonPincodeChecker-v3.exe --self-test` when Windows allows it and no other copy is open.

## Sources

The nationwide file contains 19,586 unique pincodes from 165,627 post-office records, downloaded on 12 October 2026 from https://github.com/sathishkvn1/AllIndiaPincodeDirectory/blob/main/all-india-pincode-html-csv-Ver-2026.csv . This third-party list may omit recent changes. It is a candidate list, not confirmed delivery coverage for this monitor.

Official references: https://www.indiapost.gov.in/rti/pincodelist and https://data.gov.in/catalog/all-india-pincode-directory-through-webservice . Microsoft WebView2 SDK 1.0.4258.31 was obtained from NuGet; its license is in `lib/webview2/LICENSE.txt`.
