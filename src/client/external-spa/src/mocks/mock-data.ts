/**
 * Mock data for local development (VITE_DEV_MOCK=true).
 * Provides realistic sample data so UI can be developed without a live BFF or auth.
 */

import type {
  ODataProject,
  ODataDocument,
  ODataEvent,
  ODataContact,
  ODataOrganization,
} from "../api/web-api-client";
import type { ExternalUserContextResponse } from "../auth/bff-client";

export const MOCK_USER: ExternalUserContextResponse = {
  contactId: "mock-contact-001",
  email: "jane.smith@externalfirm.com",
  projects: [
    { projectId: "mock-project-001", accessLevel: "Collaborate" },
    { projectId: "mock-project-002", accessLevel: "ViewOnly" },
  ],
};

export const MOCK_PROJECTS: ODataProject[] = [
  {
    sprk_projectid: "mock-project-001",
    sprk_name: "Acme Corp Acquisition",
    sprk_referencenumber: "MAT-2025-0042",
    sprk_description:
      "Cross-border acquisition of Acme Corp subsidiary entities across three jurisdictions. Requires regulatory approvals in EU, UK, and US.",
    sprk_issecure: true,
    sprk_status: 1,
    createdon: "2025-09-15T09:00:00Z",
    modifiedon: "2026-02-28T14:30:00Z",
  },
  {
    sprk_projectid: "mock-project-002",
    sprk_name: "Meridian Restructuring",
    sprk_referencenumber: "MAT-2025-0078",
    sprk_description:
      "Corporate restructuring and debt refinancing for Meridian Holdings group entities.",
    sprk_issecure: true,
    sprk_status: 1,
    createdon: "2025-11-01T11:00:00Z",
    modifiedon: "2026-03-10T08:15:00Z",
  },
];

export const MOCK_DOCUMENTS: Record<string, ODataDocument[]> = {
  "mock-project-001": [
    {
      sprk_documentid: "doc-001",
      sprk_name: "Share Purchase Agreement — Draft v3.docx",
      sprk_documenttype: "Agreement",
      sprk_summary:
        "Draft share purchase agreement covering transfer of 100% equity in Acme Corp subsidiaries. Key terms include purchase price of USD 250M, deferred consideration of USD 25M contingent on EBITDA targets, and standard reps & warranties with 18-month survival period.",
      _sprk_projectid_value: "mock-project-001",
      createdon: "2026-02-10T09:00:00Z",
    },
    {
      sprk_documentid: "doc-002",
      sprk_name: "Due Diligence Report — Financial.pdf",
      sprk_documenttype: "Report",
      sprk_summary:
        "Financial due diligence report prepared by external auditors. Identifies three material items: pension liability shortfall of USD 8M, deferred tax asset recoverability risk, and related party loan terms requiring arm's-length adjustment.",
      _sprk_projectid_value: "mock-project-001",
      createdon: "2026-01-28T14:00:00Z",
    },
    {
      sprk_documentid: "doc-003",
      sprk_name: "Regulatory Filing — EU Merger Control.pdf",
      sprk_documenttype: "Filing",
      sprk_summary: null,
      _sprk_projectid_value: "mock-project-001",
      createdon: "2026-02-20T10:30:00Z",
    },
  ],
  "mock-project-002": [
    {
      sprk_documentid: "doc-004",
      sprk_name: "Restructuring Term Sheet.pdf",
      sprk_documenttype: "Agreement",
      sprk_summary: "Term sheet for senior secured debt refinancing.",
      _sprk_projectid_value: "mock-project-002",
      createdon: "2026-01-15T10:00:00Z",
    },
  ],
};

export const MOCK_EVENTS: Record<string, ODataEvent[]> = {
  "mock-project-001": [
    {
      sprk_eventid: "evt-001",
      sprk_name: "Signing Deadline",
      sprk_duedate: "2026-04-15T00:00:00Z",
      sprk_status: 0,
      sprk_todoflag: false,
      _sprk_projectid_value: "mock-project-001",
      createdon: "2026-01-10T09:00:00Z",
    },
    {
      sprk_eventid: "evt-002",
      sprk_name: "Review SPA redline from counterparty",
      sprk_duedate: "2026-03-28T00:00:00Z",
      sprk_status: 0,
      sprk_todoflag: true,
      _sprk_projectid_value: "mock-project-001",
      createdon: "2026-03-15T09:00:00Z",
    },
    {
      sprk_eventid: "evt-003",
      sprk_name: "Provide tax structuring comments",
      sprk_duedate: "2026-04-02T00:00:00Z",
      sprk_status: 0,
      sprk_todoflag: true,
      _sprk_projectid_value: "mock-project-001",
      createdon: "2026-03-18T09:00:00Z",
    },
    {
      sprk_eventid: "evt-004",
      sprk_name: "Regulatory approval — EU filing",
      sprk_duedate: "2026-05-01T00:00:00Z",
      sprk_status: 0,
      sprk_todoflag: false,
      _sprk_projectid_value: "mock-project-001",
      createdon: "2026-02-01T09:00:00Z",
    },
  ],
  "mock-project-002": [
    {
      sprk_eventid: "evt-005",
      sprk_name: "Lender consent deadline",
      sprk_duedate: "2026-04-30T00:00:00Z",
      sprk_status: 0,
      sprk_todoflag: false,
      _sprk_projectid_value: "mock-project-002",
      createdon: "2026-02-01T09:00:00Z",
    },
  ],
};

export const MOCK_CONTACTS: Record<string, ODataContact[]> = {
  "mock-project-001": [
    {
      contactid: "contact-001",
      fullname: "Jane Smith",
      firstname: "Jane",
      lastname: "Smith",
      emailaddress1: "jane.smith@externalfirm.com",
      telephone1: "+1 212 555 0101",
      jobtitle: "Partner",
      _parentcustomerid_value: "org-001",
    },
    {
      contactid: "contact-002",
      fullname: "Michael Chen",
      firstname: "Michael",
      lastname: "Chen",
      emailaddress1: "m.chen@externalfirm.com",
      telephone1: "+1 212 555 0102",
      jobtitle: "Associate",
      _parentcustomerid_value: "org-001",
    },
    {
      contactid: "contact-003",
      fullname: "Sophie Müller",
      firstname: "Sophie",
      lastname: "Müller",
      emailaddress1: "s.muller@euadvisors.de",
      telephone1: "+49 89 555 0200",
      jobtitle: "Senior Advisor",
      _parentcustomerid_value: "org-002",
    },
  ],
  "mock-project-002": [
    {
      contactid: "contact-004",
      fullname: "David Park",
      firstname: "David",
      lastname: "Park",
      emailaddress1: "d.park@restructuringco.com",
      telephone1: "+1 312 555 0300",
      jobtitle: "Managing Director",
      _parentcustomerid_value: "org-003",
    },
  ],
};

export const MOCK_ORGANIZATIONS: Record<string, ODataOrganization[]> = {
  "mock-project-001": [
    {
      accountid: "org-001",
      name: "External Firm LLP",
      websiteurl: "https://externalfirm.com",
      telephone1: "+1 212 555 0100",
      address1_city: "New York",
      address1_country: "United States",
    },
    {
      accountid: "org-002",
      name: "EU Advisors GmbH",
      websiteurl: "https://euadvisors.de",
      telephone1: "+49 89 555 0200",
      address1_city: "Munich",
      address1_country: "Germany",
    },
  ],
  "mock-project-002": [
    {
      accountid: "org-003",
      name: "Restructuring Co.",
      websiteurl: null,
      telephone1: "+1 312 555 0300",
      address1_city: "Chicago",
      address1_country: "United States",
    },
  ],
};
