A **proof-of-concept (POC) flow for Adobe Acrobat Sign + SharePoint Online**.
This text plan is written so you can **recreate the flow in Power Automate** step-by-step.

Assumptions

* You have an **Adobe Acrobat Sign account** connected in Power Automate.
* You have a **SharePoint library** with columns:

  * `Status` (Choice: Draft, Pending Signature, Sent, Signed)
    ```text
    {
    "$schema": "https://developer.microsoft.com/json-schemas/sp/v2/column-formatting.schema.json",
    "elmType": "div",
    "style": {
        "padding": "4px",
        "border-radius": "6px",
        "text-align": "center",
        "font-weight": "600",
        "color": "white"
    },
    "attributes": {
        "class": "=if(@currentField == 'Pending Signature','sp-field-severity--warning', if(@currentField == 'Sent','sp-field-severity--low', if(@currentField == 'Signed','sp-field-severity--good','sp-field-severity--severeWarning')))"
    },
    "txtContent": "@currentField"
    }
    ```
    ## ✅ What this does

    * `Pending Signature` → 🟨 Yellow background
    * `Sent` → 🔵 Light blue background
    * `Signed` → 🟩 Green background
    * Any other status (e.g., `Rejected`, `Error`) → 🔴 Red background
  * `AgreementId` (Single line of text)
  * `SignedDate` (Date/Time)

---

# 🌐 Flow A: Send Document for Signature (POC)

### 1. Trigger

* **Trigger:** `When a file is created or modified (properties only)` (SharePoint)
* Configure: Select your Site Address and Library Name.

**Trigger condition (in Settings):**

```text
@and(
  equals(triggerOutputs()?['body/Status'], 'Pending Signature'),
  empty(triggerOutputs()?['body/AgreementId'])
)
```

→ This ensures the flow only runs when `Status = Pending Signature` and no agreement was created yet.

---

### 2. Get file content

* **Action:** `Get file content` (SharePoint)
* **File Identifier:** use `Identifier` from trigger.

---

### 3. Create agreement from a file and send for signature

* **Action (Adobe Sign):** `Create an agreement from a file and send for signature`
* Configure:

  * **Agreement Name:** `concat(triggerOutputs()?['body/FileLeafRef'], ' - Signature Request')`
  * **Recipients:**

    * `Recipient Email`: *hardcode a test email first* (later you can bind it to a SharePoint column like `Signer.Email`).
    * `Role`: `SIGNER`
  * **File Content:** `File Content` from **Get file content** action.
  * **File Name:** `triggerOutputs()?['body/FileLeafRef']`
  * **Signature Type:** `ESIGN`
  * **Message:** `"Please sign this document via Adobe Sign."`

This returns an **AgreementId**.

---

### 4. Update file properties

* **Action:** `Update file properties` (SharePoint)
* Set:

  * `Status = Sent`
  * `AgreementId =` `Agreement Id` (from Adobe Sign action)

---

✅ At this point, your SharePoint library knows which AgreementId is tied to which document.

---

# 🌐 Flow B: Update SharePoint when signature is completed

### 1. Trigger

* **Trigger (Adobe Sign):** `When an agreement workflow is completed`

  * Connection: Adobe Acrobat Sign
  * This will fire when the document is signed.

---

### 2. Get files (properties only)

* **Action:** `Get files (properties only)` (SharePoint)
* Configure: Site + Library.
* Add an OData filter:

  ```text
  AgreementId eq '@{triggerOutputs()?['body/agreementId']}'
  ```

  → Replace `agreementId` with the field name from the trigger body.

---

### 3. Condition (check if file found)

* If **Yes** (file exists): proceed.

---

### 4. Switch Control: Agreement Status

Insert a **Switch** on `@{triggerOutputs()?['body/status']}`.

#### Case 1: `"COMPLETED"` (Signed ✅)

* **Update file properties (SharePoint):**

  * `Status = Signed`
  * `SignedDate = utcNow()`
* **Get combined document of an agreement** (Adobe Sign).
* **Create file (SharePoint):** Save as `[filename]_signed.pdf` with the signed PDF content.

#### Case 2: `"DECLINED"` (❌ Signer rejected)

* **Update file properties (SharePoint):**

  * `Status = Declined`
  * Add a `Notes` column with `"Agreement was declined by signer"`
  * Send Teams/Email notification to document owner.

#### Case 3: `"EXPIRED"` (⌛ Not signed in time)

* **Update file properties (SharePoint):**

  * `Status = Expired`
  * Add `Notes = 'Agreement expired before completion'`.
  * Send notification to document owner to re-send.

#### Default Case (Other statuses)

* **Update file properties (SharePoint):**

  * `Status = Unknown`
  * Log to a monitoring list or send admin notification.

---

# ⚡ Expressions you’ll need

* Switch on **status**:

  ```text
  @{triggerOutputs()?['body/status']}
  ```

* Update SignedDate:

  ```text
  utcNow()
  ```

* For file naming when saving signed copy:

  ```text
  concat(triggerOutputs()?['body/name'], '_signed.pdf')
  ```

---

### 5. Get signed document

* **Action (Adobe Sign):** `Get the combined document of an agreement`
* Input: `AgreementId` from trigger.

---

### 6. Create file (save signed copy in SharePoint)

* **Action:** `Create file` (SharePoint)
* Configure:

  * **Folder Path:** Same library (or subfolder `Signed`)
  * **File Name:** `concat(triggerOutputs()?['body/name'], '_signed.pdf')`
  * **File Content:** `File Content` from Adobe Sign action.

---

# Additional Processes

---

# 🌐 Addendum Flow B with Notifications

We’ll build on the **Signed / Declined / Expired** handling from before.

---

## 🔹 1. After "Get files (properties only)"

The returned item(s) include the **Created By** (internal column `Author`). That will be the **document owner** you notify.

* Use **Get user profile (V2)** (Office 365 Users connector)

  * **User (UPN):** `Author Email` from SharePoint item (dynamic content).
  * This gives you the owner’s display name, Teams ID, and email.

---

## 🔹 2. Inside Each Switch Case

### Case 1: `"COMPLETED"` → Signed ✅

* **Update file properties** → Status = `Signed`, SignedDate = `utcNow()`.
* **Get combined document of an agreement** (Adobe Sign).
* **Create file (SharePoint):** `[filename]_signed.pdf`.

🔔 **Send Notification:**

* **Send an email (V2)** (Office 365 Outlook)

  * To: `Author Email`
  * Subject: `"Your document has been signed"`
  * Body:

    ```html
    Hi @{outputs('Get_user_profile_(V2)')?['body/displayName']},<br><br>
    Your document <b>@{triggerOutputs()?['body/name']}</b> has been <b>signed</b> successfully.<br>
    You can view it here: <a href="@{outputs('Create_file')?['body/Path']}">Signed Copy</a><br><br>
    Regards,<br>Document Signing System
    ```
* **Post message in a chat or channel (Teams)**

  * Recipient: `Author Email`
  * Message: `"Your document '@{triggerOutputs()?['body/name']}' has been signed and saved in SharePoint."`

---

### Case 2: `"DECLINED"` → Declined ❌

* **Update file properties:** Status = `Declined`.
* (Optional) Set a Notes column: `"Signer declined to sign."`

🔔 **Send Notification:**

* Email subject: `"Your document was declined"`
* Teams message: `"Your document '@{triggerOutputs()?['body/name']}' was declined by the signer."`

---

### Case 3: `"EXPIRED"` → Expired ⌛

* **Update file properties:** Status = `Expired`.
* (Optional) Set Notes: `"Agreement expired before signing."`

🔔 **Send Notification:**

* Email subject: `"Your document has expired"`
* Teams message: `"Your document '@{triggerOutputs()?['body/name']}' expired before it was signed."`

---

### Default Case → Unknown/Other

* **Update file properties:** Status = `Unknown`.
* 🔔 Notify admin or owner: `"Agreement status changed to @{triggerOutputs()?['body/status']} for document '@{triggerOutputs()?['body/name']}'."`

---

# ⚡ Expressions You’ll Need

* Owner’s email from SharePoint item:

  ```text
  @{items('Apply_to_each')?['Author/Email']}
  ```

* Signed copy link (if you save to SharePoint):

  ```text
  @{outputs('Create_file')?['body/Path']}
  ```

---

# 🏁 End Result

Now your **Flow B**:

* Updates SharePoint metadata ✅
* Saves signed copy ✅
* Notifies the owner **via Outlook and Teams** of any terminal status:

  * Signed → “Your doc was signed” with a link.
  * Declined → “Your doc was declined.”
  * Expired → “Your doc expired.”
  * Other → “Status changed to X.”

---

Now your SharePoint library will:

* ✅ Move docs to **Signed** when completed & save signed PDF copy.
* ❌ Mark docs as **Declined** when signer rejects.
* ⌛ Mark docs as **Expired** if time runs out.
* 🔔 Notify owners so they can take action.

---

# 🔎 Testing this POC

1. Upload a PDF into the library, set `Status = Pending Signature`.
2. Flow A fires → sends via Adobe Sign. SharePoint item updates to `Sent`.
3. Sign via Adobe email.
4. Flow B fires → updates `Status = Signed`, fills `SignedDate`, saves `_signed.pdf` into library.

---

# ⚡ Key Notes

* In production: replace hardcoded email with `Signer.Email` column from SharePoint.
* You can expand Flow B to handle `Declined` or `Expired` statuses.
* Use `utcNow()` expression for timestamps.


