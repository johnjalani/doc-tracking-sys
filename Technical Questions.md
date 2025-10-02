# 📋 Technical Requirement Questions (Including Power Automate)

### 1. **Document Storage & Metadata (SharePoint)**

* What metadata columns must Power Automate update (e.g., Status, SignedDate, AgreementId)?
* Should flows automatically generate metadata (e.g., unique tracking IDs)?
* Will Power Automate be responsible for moving signed files to an archive library or folder?

---

### 2. **E-Signature Integration**

* Which connector will we use in Power Automate (Adobe Sign, DocuSign, or Microsoft Purview eSign)?
* Do we need custom API calls via **HTTP connector** if the standard connector doesn’t meet requirements?
* Should Power Automate handle multi-signer scenarios (parallel vs sequential signing)?
* Do we require storing **Adobe Sign transaction IDs** in SharePoint for auditing?

---

### 3. **Flow Triggering & Automation**

* Should the flow trigger **automatically when a file is uploaded/modified** in SharePoint?
* Do we want a **manual trigger** (e.g., “Send for Signature” button in SharePoint/Teams)?
* Should Power Automate run **synchronously** (wait for results) or **asynchronously** (update status when complete)?
* Do we need scheduled flows to check for expired agreements daily?

---

### 4. **Notifications & Tracking**

* What notification channels should Power Automate support (Teams Adaptive Cards, Outlook email, push notifications)?
* Should Teams messages include **action buttons** (Approve/Reject, Open in SharePoint)?
* Do we need centralized reporting (e.g., Power BI dashboard fed by Power Automate logs)?
* Should flows log every transaction in a **separate SharePoint “Audit List”**?

---

### 5. **Security & Governance**

* Who can create or edit Power Automate flows (developers, power users, admins)?
* Do we need **environment segregation** (Dev, Test, Prod) for flows?
* Should sensitive data (e.g., signed PDFs, signer emails) be encrypted before being stored?
* Will Power Automate use **service accounts** for running flows, or impersonate the uploader?

---

### 6. **Scalability & Performance**

* How many documents per day are expected to go through the flow?
* Do we need **premium connectors** (Adobe Sign, DocuSign, etc.), and how many licenses are required?
* Should Power Automate handle **bulk sends** (looping through multiple recipients)?
* What retry policy should be configured for failed API calls (exponential backoff, custom retries)?

---

### 7. **Integration & Extensibility**

* Should Power Automate integrate with external apps (ERP/CRM/HR systems)?
* Do we need to expose **Power Automate as an API endpoint** for external systems to initiate signing requests?
* Should Power Automate push logs into **Dataverse** or a SQL DB for analytics?
* Do we require custom connectors if Adobe Sign/DocuSign standard connectors lack features?

---

### 8. **Exception Handling**

* How should Power Automate handle **failed API calls** (retry, alert admin, log to SharePoint)?
* What should happen if a document is **declined or expired** — should Power Automate send reminders, update SharePoint, notify owners?
* Should errors be logged to a **SharePoint list or monitoring dashboard** for admin review?
* Should there be an **escalation workflow** if a document is not signed within a given SLA?
