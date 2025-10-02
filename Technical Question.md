---

# 📋 Technical Requirement Questions

### 1. **Document Storage & Metadata**

* What document types (PDF, Word, Excel) will be uploaded and tracked?
* Should we enforce a file naming convention?
* Which metadata columns do we need in SharePoint (e.g., Status, AgreementId, Owner, SignedDate)?
* Should version history be maintained for documents?

---

### 2. **E-Signature Integration**

* Which e-signature provider do we plan to use (Adobe Sign, DocuSign, or Microsoft’s native integration)?
* How many signers can a document have — single signer, multiple, or sequential routing?
* Do we require signer authentication methods (e.g., email, SMS OTP)?
* Should the signed copy replace the original file or be stored as a new version/document?
* What happens if the document is declined or expires?

---

### 3. **Workflow & Automation**

* Should the signature request be triggered **automatically** on upload, or **manually** by a user action (button/Flow)?
* Do we need approval steps before sending for signature?
* How should document status be updated in SharePoint (automated via Flow, or manual updates)?
* Should reminders be sent automatically if a signer does not respond?

---

### 4. **Notifications & Tracking**

* Which users should receive notifications (Owner, Signer, Admin)?
* Should notifications be via **Teams Adaptive Cards**, **email alerts**, or both?
* Do we need dashboards/reports to track signed/unsigned/expired documents?
* Should SharePoint views use conditional formatting (e.g., color-coded statuses)?

---

### 5. **Security & Compliance**

* Who should have permission to upload/send documents for signature?
* Should signed documents be locked from further edits in SharePoint?
* Do we need audit trails (who uploaded, who signed, timestamps)?
* Are there retention policies (e.g., keep signed docs for 7 years)?

---

### 6. **Scalability & Performance**

* How many documents per month do we expect to process?
* Do we need bulk sending (e.g., send one contract to 100 recipients)?
* Should the system handle multilingual documents (e.g., English, Tagalog)?

---

### 7. **Integration & Extensibility**

* Should the solution integrate with other systems (ERP, CRM, HR)?
* Do we need APIs exposed for external apps to fetch document status?
* Should we allow mobile signing workflows?

---

### 8. **Exception Handling**

* What should happen if a signature request fails (e.g., Adobe Sign API error)?
* How do we handle documents that are **rejected** or **expired**?
* Should users be able to re-send expired requests?

---

✅ These questions cover **functional, technical, and operational aspects** to make sure you don’t miss hidden requirements.

---

Would you like me to also create a **requirements traceability matrix (RTM template)** for this project so you can track these questions against business needs and technical solutions?
