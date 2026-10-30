namespace Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;

/// <summary>
/// Recorded Content Understanding analyze results (ebilling-r1 task 043). Source: the task 005 spike's real responses for
/// synthetic invoice B-0002 (a GBP invoice, two pages, US Letter 8.5 x 11 in), from
/// <c>projects/ebilling-r1/notes/evaluation-set/local/spike-005/{cp,c}/B-0002.json</c>, trimmed by
/// <c>make_fixtures.py</c> (task 043 scratch): spans, markdown, paragraphs, sections and tables removed; the first 3 time
/// entries, 2 expenses and 3 timekeeper-summary rows kept; every kept value, confidence, source and page size is as the
/// service returned it. <see cref="PreviewB0002"/> is the preview analyzer on 2026-06-01-preview (carries
/// <c>metadata.pageCount</c> as a string); <see cref="GaB0002"/> is the GA analyzer on 2025-11-01 (no metadata).
/// The analyzer ids inside are the spike's v1 analyzers: the v2 field schema is the same.
/// </summary>
internal static class ContentUnderstandingRecordedResults
{
    public const string PreviewB0002 =
        """
        {
          "id": "00000000-0000-0000-0000-000000000043",
          "status": "Succeeded",
          "result": {
            "analyzerId": "sprk_invoice_v1p",
            "apiVersion": "2026-06-01-preview",
            "warnings": [],
            "contents": [
              {
                "kind": "document",
                "startPageNumber": 1,
                "endPageNumber": 2,
                "unit": "inch",
                "pages": [
                  {"pageNumber": 1, "angle": 0, "width": 8.5, "height": 11},
                  {"pageNumber": 2, "angle": 0, "width": 8.5, "height": 11}
                ],
                "fields": {
                  "FirmName": {"type": "string", "valueString": "Ravenscroft and Vance LLP", "confidence": 0.947, "source": "D(1,0.5469,0.7165,3.3120,0.7111,3.3124,0.9227,0.5473,0.9281)"},
                  "FirmInvoiceNumber": {"type": "string", "valueString": "2025023029", "confidence": 0.993, "source": "D(1,6.5410,1.7108,7.2507,1.7098,7.2507,1.8405,6.5410,1.8401)"},
                  "InvoiceDate": {"type": "date", "valueDate": "2025-08-01", "confidence": 0.993, "source": "D(1,6.5410,1.8794,7.1843,1.8786,7.1843,2.0088,6.5410,2.0079)"},
                  "BillingPeriodStart": {"type": "date", "valueDate": "2025-07-01", "confidence": 0.946, "source": "D(1,6.5408,2.0378,7.9231,2.0362,7.9233,2.1730,6.5410,2.1745)"},
                  "BillingPeriodEnd": {"type": "date", "valueDate": "2025-07-31", "confidence": 0.946, "source": "D(1,6.5408,2.0378,7.9231,2.0362,7.9233,2.1730,6.5410,2.1745)"},
                  "ClientName": {"type": "string", "valueString": "Client Legal Department", "confidence": 0.961, "source": "D(1,0.9653,1.9250,2.3349,1.9280,2.3346,2.0800,0.9650,2.0770)"},
                  "MatterName": {"type": "string", "valueString": "Professional services", "confidence": 0.839, "source": "D(1,0.7787,2.0974,1.9738,2.0998,1.9735,2.2399,0.7784,2.2375)"},
                  "FirmMatterReference": {"type": "string", "valueString": "13080-0029", "confidence": 0.994, "source": "D(1,6.5535,2.2115,7.2217,2.2117,7.2217,2.3397,6.5535,2.3393)"},
                  "PurchaseOrderAsQuoted": {"type": "string", "valueString": "PO-4550267", "confidence": 0.963, "source": "D(1,6.5410,2.3702,7.2634,2.3712,7.2632,2.5061,6.5408,2.5051)"},
                  "FeesTotal": {"type": "number", "valueNumber": 10119, "confidence": 0.965, "source": "D(2,7.2342,0.7904,7.9497,0.7985,7.9479,0.9502,7.2324,0.9421)"},
                  "ExpensesTotal": {"type": "number", "valueNumber": 2635.52, "confidence": 0.967, "source": "D(2,7.3086,1.0224,7.9480,1.0216,7.9482,1.1658,7.3088,1.1666)"},
                  "AdjustmentsTotal": {"type": "number", "valueNumber": 0, "confidence": 0.977, "source": "D(2,7.5828,1.2402,7.9448,1.2428,7.9438,1.3846,7.5818,1.3820)"},
                  "TotalDue": {"type": "number", "valueNumber": 12754.52, "confidence": 0.963, "source": "D(2,7.1622,1.4539,7.9521,1.4471,7.9535,1.6138,7.1636,1.6206)"},
                  "Currency": {"type": "string", "valueString": "GBP", "confidence": 0.979, "source": "D(1,6.5410,2.5423,6.8191,2.5446,6.8191,2.6709,6.5410,2.6685)"},
                  "TimeEntries": {
                    "type": "array",
                    "valueArray": [
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-07-02", "confidence": 0.839, "source": "D(1,0.5753,3.2078,1.2202,3.2067,1.2202,3.3517,0.5753,3.3528)"},
                          "TimekeeperAsBilled": {"type": "string", "valueString": "Naomi Lockhart", "confidence": 0.81, "source": "D(1,1.3416,3.2185,1.7411,3.2176,1.7411,3.3531,1.3416,3.3531);D(1,1.3468,3.3918,1.8729,3.3918,1.8729,3.5294,1.3468,3.5265)"},
                          "TitleAsBilled": {"type": "string", "confidence": 0.971},
                          "Hours": {"type": "number", "valueNumber": 2.1, "confidence": 0.532, "source": "D(1,6.1011,3.2188,6.3584,3.2137,6.3584,3.3466,6.1011,3.3516)"},
                          "Rate": {"type": "number", "valueNumber": 260, "confidence": 0.839, "source": "D(1,6.6489,3.2160,7.0432,3.2146,7.0432,3.3515,6.6489,3.3520)"},
                          "Amount": {"type": "number", "valueNumber": 546, "confidence": 0.819, "source": "D(1,7.4332,3.2133,7.9065,3.2129,7.9066,3.3527,7.4333,3.3531)"},
                          "Narrative": {"type": "string", "valueString": "[L430/A104] Revise complaint after review by lead counsel", "confidence": 0.755, "source": "D(1,2.2931,3.2056,5.6528,3.2056,5.6528,3.3736,2.2931,3.3736)"},
                          "TaskCodeAsBilled": {"type": "string", "valueString": "L430", "confidence": 0.839, "source": "D(1,2.3373,3.2059,2.6329,3.2058,2.6329,3.3729,2.3373,3.3726)"},
                          "ActivityCodeAsBilled": {"type": "string", "valueString": "A104", "confidence": 0.906, "source": "D(1,2.6716,3.2058,2.9811,3.2057,2.9811,3.3732,2.6716,3.3729)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-07-02", "confidence": 0.874, "source": "D(1,0.5743,3.6386,1.2202,3.6334,1.2202,3.7805,0.5743,3.7863)"},
                          "TimekeeperAsBilled": {"type": "string", "valueString": "Katya Rosenthal", "confidence": 0.813, "source": "D(1,1.3437,3.6492,1.6954,3.6522,1.6954,3.7975,1.3437,3.7968);D(1,1.3468,3.8215,1.9206,3.8181,1.9206,3.9612,1.3468,3.9612)"},
                          "TitleAsBilled": {"type": "string", "confidence": 0.971},
                          "Hours": {"type": "number", "valueNumber": 2, "confidence": 0.581, "source": "D(1,6.1011,3.6470,6.3584,3.6470,6.3584,3.7812,6.1011,3.7812)"},
                          "Rate": {"type": "number", "valueNumber": 865, "confidence": 0.841, "source": "D(1,6.6531,3.6467,7.0432,3.6458,7.0432,3.7815,6.6531,3.7825)"},
                          "Amount": {"type": "number", "valueNumber": 1730, "confidence": 0.82, "source": "D(1,7.3295,3.6416,7.9065,3.6414,7.9066,3.7877,7.3296,3.7879)"},
                          "Narrative": {"type": "string", "valueString": "[L120/A101] Plan strategy for upcoming phase of the employment claim with team", "confidence": 0.753, "source": "D(1,2.2910,3.6229,5.2882,3.6330,5.2875,3.8145,2.2904,3.8044);D(1,2.2869,3.8200,3.9368,3.8226,3.9366,3.9799,2.2867,3.9773)"},
                          "TaskCodeAsBilled": {"type": "string", "valueString": "L120", "confidence": 0.839, "source": "D(1,2.3385,3.6345,2.6320,3.6356,2.6320,3.8052,2.3385,3.8040)"},
                          "ActivityCodeAsBilled": {"type": "string", "valueString": "A101", "confidence": 0.839, "source": "D(1,2.6740,3.6358,2.9675,3.6369,2.9675,3.8066,2.6740,3.8054)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-07-03", "confidence": 0.865, "source": "D(1,0.5743,4.0657,1.2213,4.0649,1.2213,4.2099,0.5743,4.2107)"},
                          "TimekeeperAsBilled": {"type": "string", "valueString": "Imani Wickham", "confidence": 0.801, "source": "D(1,1.3437,4.0685,1.6871,4.0684,1.6871,4.2027,1.3437,4.2028);D(1,1.3510,4.2575,1.9030,4.2584,1.9030,4.3956,1.3510,4.3957)"},
                          "TitleAsBilled": {"type": "string", "confidence": 0.971},
                          "Hours": {"type": "number", "valueNumber": 2.3, "confidence": 0.579, "source": "D(1,6.1011,4.0686,6.3584,4.0686,6.3584,4.2029,6.1011,4.2029)"},
                          "Rate": {"type": "number", "valueNumber": 450, "confidence": 0.829, "source": "D(1,6.6489,4.0690,7.0432,4.0714,7.0432,4.2057,6.6489,4.2033)"},
                          "Amount": {"type": "number", "valueNumber": 1035, "confidence": 0.814, "source": "D(1,7.3320,4.0670,7.9065,4.0608,7.9082,4.2139,7.3337,4.2201)"},
                          "Narrative": {"type": "string", "valueString": "[L190/A104] Review court docket and calendar of pending deadlines for the construction contract", "confidence": 0.813, "source": "D(1,2.2931,4.0615,5.6031,4.0636,5.6030,4.2344,2.2930,4.2323);D(1,2.2889,4.2496,4.4452,4.2509,4.4451,4.4050,2.2888,4.4037)"},
                          "TaskCodeAsBilled": {"type": "string", "valueString": "L190", "confidence": 0.839, "source": "D(1,2.3356,4.0625,2.6332,4.0626,2.6332,4.2317,2.3356,4.2315)"},
                          "ActivityCodeAsBilled": {"type": "string", "valueString": "A104", "confidence": 0.914, "source": "D(1,2.6757,4.0626,2.9817,4.0627,2.9817,4.2319,2.6757,4.2317)"}
                        }
                      }
                    ]
                  },
                  "Expenses": {
                    "type": "array",
                    "valueArray": [
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-07-07", "confidence": 0.879, "source": "D(1,0.5743,8.0825,1.2171,8.0790,1.2171,8.2224,0.5743,8.2227)"},
                          "Description": {"type": "string", "valueString": "[E110] Airfare to Atlanta for deposition", "confidence": 0.722, "source": "D(1,2.2908,8.0744,4.5281,8.0718,4.5283,8.2470,2.2910,8.2496)"},
                          "Quantity": {"type": "number", "valueNumber": 1, "confidence": 0.721, "source": "D(1,6.2878,8.0889,6.3418,8.0889,6.3418,8.2070,6.2878,8.2070)"},
                          "UnitCost": {"type": "number", "valueNumber": 350.71, "confidence": 0.857, "source": "D(1,6.6531,8.0792,7.0349,8.0826,7.0349,8.2115,6.6531,8.2081)"},
                          "Amount": {"type": "number", "valueNumber": 350.71, "confidence": 0.778, "source": "D(1,7.4333,8.0773,7.8944,8.0785,7.8940,8.2190,7.4329,8.2178)"},
                          "ExpenseCodeAsBilled": {"type": "string", "valueString": "E110", "confidence": 0.839, "source": "D(1,2.3338,8.0786,2.6306,8.0779,2.6306,8.2483,2.3338,8.2479)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-07-08", "confidence": 0.888, "source": "D(1,0.5728,8.3359,1.2181,8.3359,1.2181,8.4756,0.5733,8.4756)"},
                          "Description": {"type": "string", "valueString": "[E115] Court reporter and transcript - deposition", "confidence": 0.727, "source": "D(1,2.2952,8.3279,5.0097,8.3306,5.0095,8.4998,2.2950,8.4971)"},
                          "Quantity": {"type": "number", "valueNumber": 1, "confidence": 0.725, "source": "D(1,6.2837,8.3467,6.3418,8.3467,6.3418,8.4648,6.2837,8.4648)"},
                          "UnitCost": {"type": "number", "valueNumber": 1603.35, "confidence": 0.839, "source": "D(1,6.5535,8.3359,7.0391,8.3359,7.0391,8.4756,6.5535,8.4756)"},
                          "Amount": {"type": "number", "valueNumber": 1603.35, "confidence": 0.793, "source": "D(1,7.3337,8.3304,7.9026,8.3316,7.9023,8.4819,7.3334,8.4807)"},
                          "ExpenseCodeAsBilled": {"type": "string", "valueString": "E115", "confidence": 0.832, "source": "D(1,2.3313,8.3304,2.6261,8.3299,2.6261,8.4959,2.3313,8.4949)"}
                        }
                      }
                    ]
                  },
                  "TimekeeperSummary": {
                    "type": "array",
                    "valueArray": [
                      {
                        "type": "object",
                        "valueObject": {
                          "Name": {"type": "string", "valueString": "Dalia Abernathy", "confidence": 0.699, "source": "D(2,0.5459,2.6586,1.4844,2.6690,1.4826,2.8282,0.5441,2.8178)"},
                          "Title": {"type": "string", "valueString": "Of Counsel", "confidence": 0.839, "source": "D(2,2.9094,2.6608,3.5611,2.6615,3.5610,2.8046,2.9093,2.8039)"},
                          "Hours": {"type": "number", "valueNumber": 2.4, "confidence": 0.513, "source": "D(2,5.0220,2.6636,5.2876,2.6633,5.2876,2.7949,5.0220,2.7952)"},
                          "Rate": {"type": "number", "valueNumber": 780, "confidence": 0.888, "source": "D(2,4.0591,2.6633,4.4451,2.6625,4.4451,2.8010,4.0591,2.8010)"},
                          "Amount": {"type": "number", "valueNumber": 1872, "confidence": 0.769, "source": "D(2,5.8271,2.6596,6.3932,2.6657,6.3916,2.8143,5.8255,2.8082)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Name": {"type": "string", "valueString": "Imani Wickham", "confidence": 0.654, "source": "D(2,0.5432,2.8572,1.4609,2.8574,1.4609,3.0029,0.5432,3.0027)"},
                          "Title": {"type": "string", "valueString": "Associate", "confidence": 0.915, "source": "D(2,2.9053,2.8623,3.4635,2.8605,3.4635,2.9975,2.9053,2.9993)"},
                          "Hours": {"type": "number", "valueNumber": 7.1, "confidence": 0.553, "source": "D(2,5.0303,2.8640,5.2793,2.8572,5.2793,2.9927,5.0303,2.9962)"},
                          "Rate": {"type": "number", "valueNumber": 450, "confidence": 0.898, "source": "D(2,4.0508,2.8572,4.4451,2.8557,4.4451,2.9912,4.0508,2.9917)"},
                          "Amount": {"type": "number", "valueNumber": 3195, "confidence": 0.798, "source": "D(2,5.8229,2.8553,6.3916,2.8550,6.3917,3.0051,5.8230,3.0054)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Name": {"type": "string", "valueString": "Katya Rosenthal", "confidence": 0.654, "source": "D(2,0.5432,3.0476,1.4861,3.0492,1.4858,3.2101,0.5429,3.2085)"},
                          "Title": {"type": "string", "valueString": "Partner", "confidence": 0.911, "source": "D(2,2.9053,3.0588,3.3245,3.0629,3.3245,3.1959,2.9053,3.1935)"},
                          "Hours": {"type": "number", "valueNumber": 3.2, "confidence": 0.557, "source": "D(2,5.0261,3.0569,5.2876,3.0554,5.2876,3.1931,5.0261,3.1931)"},
                          "Rate": {"type": "number", "valueNumber": 865, "confidence": 0.888, "source": "D(2,4.0529,3.0517,4.4451,3.0494,4.4451,3.1891,4.0529,3.1913)"},
                          "Amount": {"type": "number", "valueNumber": 2768, "confidence": 0.798, "source": "D(2,5.8230,3.0504,6.3918,3.0511,6.3916,3.2009,5.8228,3.2002)"}
                        }
                      }
                    ]
                  }
                },
                "metadata": {"contentType": "application/pdf", "pageCount": "2"},
                "analyzerId": "sprk_invoice_v1p"
              }
            ]
          }
        }
        """;

    public const string GaB0002 =
        """
        {
          "id": "00000000-0000-0000-0000-000000000043",
          "status": "Succeeded",
          "result": {
            "analyzerId": "sprk_invoice_v1",
            "apiVersion": "2025-11-01",
            "warnings": [],
            "contents": [
              {
                "kind": "document",
                "startPageNumber": 1,
                "endPageNumber": 2,
                "unit": "inch",
                "pages": [
                  {"pageNumber": 1, "angle": 0, "width": 8.5, "height": 11},
                  {"pageNumber": 2, "angle": 0, "width": 8.5, "height": 11}
                ],
                "fields": {
                  "FirmName": {"type": "string", "valueString": "Ravenscroft and Vance LLP", "confidence": 0.95, "source": "D(1,0.5469,0.7165,3.3120,0.7111,3.3124,0.9227,0.5473,0.9281)"},
                  "FirmInvoiceNumber": {"type": "string", "valueString": "2025023029", "confidence": 0.988, "source": "D(1,6.5410,1.7108,7.2507,1.7098,7.2507,1.8405,6.5410,1.8401)"},
                  "InvoiceDate": {"type": "date", "valueDate": "2025-01-08", "confidence": 0.992, "source": "D(1,6.5410,1.8794,7.1843,1.8786,7.1843,2.0088,6.5410,2.0079)"},
                  "BillingPeriodStart": {"type": "date", "valueDate": "2025-01-07", "confidence": 0.892, "source": "D(1,6.5408,2.0378,7.9231,2.0362,7.9233,2.1730,6.5410,2.1745)"},
                  "BillingPeriodEnd": {"type": "date", "valueDate": "2025-07-31", "confidence": 0.892, "source": "D(1,6.5408,2.0378,7.9231,2.0362,7.9233,2.1730,6.5410,2.1745)"},
                  "ClientName": {"type": "string", "valueString": "Client Legal Department", "confidence": 0.785, "source": "D(1,0.9653,1.9250,2.3349,1.9280,2.3346,2.0800,0.9650,2.0770)"},
                  "MatterName": {"type": "string", "valueString": "Professional services", "confidence": 0.881, "source": "D(1,0.7787,2.0974,1.9738,2.0998,1.9735,2.2399,0.7784,2.2375)"},
                  "FirmMatterReference": {"type": "string", "valueString": "13080-0029", "confidence": 0.992, "source": "D(1,6.5535,2.2115,7.2217,2.2117,7.2217,2.3397,6.5535,2.3393)"},
                  "PurchaseOrderAsQuoted": {"type": "string", "valueString": "PO-4550267", "confidence": 0.952, "source": "D(1,6.5410,2.3702,7.2634,2.3712,7.2632,2.5061,6.5408,2.5051)"},
                  "FeesTotal": {"type": "number", "valueNumber": 10119, "confidence": 0.953, "source": "D(2,7.2342,0.7904,7.9497,0.7985,7.9479,0.9502,7.2324,0.9421)"},
                  "ExpensesTotal": {"type": "number", "valueNumber": 2635.52, "confidence": 0.955, "source": "D(2,7.3086,1.0224,7.9480,1.0216,7.9482,1.1658,7.3088,1.1666)"},
                  "AdjustmentsTotal": {"type": "number", "valueNumber": 0, "confidence": 0.958, "source": "D(2,7.5828,1.2402,7.9448,1.2428,7.9438,1.3846,7.5818,1.3820)"},
                  "TotalDue": {"type": "number", "valueNumber": 12754.52, "confidence": 0.953, "source": "D(2,7.1622,1.4539,7.9521,1.4471,7.9535,1.6138,7.1636,1.6206)"},
                  "Currency": {"type": "string", "valueString": "GBP", "confidence": 0.88, "source": "D(1,6.5410,2.5423,6.8191,2.5446,6.8191,2.6709,6.5410,2.6685);D(2,7.2342,0.7904,7.9497,0.7985,7.9479,0.9502,7.2324,0.9421)"},
                  "TimeEntries": {
                    "type": "array",
                    "valueArray": [
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-02-07", "confidence": 0.809, "source": "D(1,0.5753,3.2078,1.2202,3.2067,1.2202,3.3517,0.5753,3.3528)"},
                          "TimekeeperAsBilled": {"type": "string", "valueString": "Naomi Lockhart", "confidence": 0.884, "source": "D(1,1.3416,3.2185,1.7411,3.2176,1.7411,3.3531,1.3416,3.3531);D(1,1.3468,3.3918,1.8729,3.3918,1.8729,3.5294,1.3468,3.5265)"},
                          "TitleAsBilled": {"type": "string", "confidence": 0.856},
                          "Hours": {"type": "number", "valueNumber": 2.1, "confidence": 0.609, "source": "D(1,6.1011,3.2188,6.3584,3.2137,6.3584,3.3466,6.1011,3.3516)"},
                          "Rate": {"type": "number", "valueNumber": 260, "confidence": 0.872, "source": "D(1,6.6489,3.2160,7.0432,3.2146,7.0432,3.3515,6.6489,3.3520)"},
                          "Amount": {"type": "number", "valueNumber": 546, "confidence": 0.798, "source": "D(1,7.4332,3.2133,7.9065,3.2129,7.9066,3.3527,7.4333,3.3531)"},
                          "Narrative": {"type": "string", "valueString": "[L430/A104] Revise complaint after review by lead counsel", "confidence": 0.797, "source": "D(1,2.2931,3.2056,5.6528,3.2056,5.6528,3.3736,2.2931,3.3736)"},
                          "TaskCodeAsBilled": {"type": "string", "valueString": "L430", "confidence": 0.711, "source": "D(1,2.3373,3.2059,2.6329,3.2058,2.6329,3.3729,2.3373,3.3726)"},
                          "ActivityCodeAsBilled": {"type": "string", "valueString": "A104", "confidence": 0.847, "source": "D(1,2.6716,3.2058,2.9811,3.2057,2.9811,3.3732,2.6716,3.3729)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-02-07", "confidence": 0.8, "source": "D(1,0.5743,3.6386,1.2202,3.6334,1.2202,3.7805,0.5743,3.7863)"},
                          "TimekeeperAsBilled": {"type": "string", "valueString": "Katya Rosenthal", "confidence": 0.863, "source": "D(1,1.3437,3.6492,1.6954,3.6522,1.6954,3.7975,1.3437,3.7968);D(1,1.3468,3.8215,1.9206,3.8181,1.9206,3.9612,1.3468,3.9612)"},
                          "TitleAsBilled": {"type": "string", "confidence": 0.856},
                          "Hours": {"type": "number", "valueNumber": 2, "confidence": 0.611, "source": "D(1,6.1011,3.6470,6.3584,3.6470,6.3584,3.7812,6.1011,3.7812)"},
                          "Rate": {"type": "number", "valueNumber": 865, "confidence": 0.85, "source": "D(1,6.6531,3.6467,7.0432,3.6458,7.0432,3.7815,6.6531,3.7825)"},
                          "Amount": {"type": "number", "valueNumber": 1730, "confidence": 0.796, "source": "D(1,7.3295,3.6416,7.9065,3.6414,7.9066,3.7877,7.3296,3.7879)"},
                          "Narrative": {"type": "string", "valueString": "[L120/A101] Plan strategy for upcoming phase of the employment claim with team", "confidence": 0.858, "source": "D(1,2.2910,3.6229,5.2882,3.6330,5.2875,3.8145,2.2904,3.8044);D(1,2.2869,3.8200,3.9368,3.8226,3.9366,3.9799,2.2867,3.9773)"},
                          "TaskCodeAsBilled": {"type": "string", "valueString": "L120", "confidence": 0.781, "source": "D(1,2.3385,3.6345,2.6320,3.6356,2.6320,3.8052,2.3385,3.8040)"},
                          "ActivityCodeAsBilled": {"type": "string", "valueString": "A101", "confidence": 0.781, "source": "D(1,2.6740,3.6358,2.9675,3.6369,2.9675,3.8066,2.6740,3.8054)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-03-07", "confidence": 0.797, "source": "D(1,0.5743,4.0657,1.2213,4.0649,1.2213,4.2099,0.5743,4.2107)"},
                          "TimekeeperAsBilled": {"type": "string", "valueString": "Imani Wickham", "confidence": 0.862, "source": "D(1,1.3437,4.0685,1.6871,4.0684,1.6871,4.2027,1.3437,4.2028);D(1,1.3510,4.2575,1.9030,4.2584,1.9030,4.3956,1.3510,4.3957)"},
                          "TitleAsBilled": {"type": "string", "confidence": 0.856},
                          "Hours": {"type": "number", "valueNumber": 2.3, "confidence": 0.598, "source": "D(1,6.1011,4.0686,6.3584,4.0686,6.3584,4.2029,6.1011,4.2029)"},
                          "Rate": {"type": "number", "valueNumber": 450, "confidence": 0.837, "source": "D(1,6.6489,4.0690,7.0432,4.0714,7.0432,4.2057,6.6489,4.2033)"},
                          "Amount": {"type": "number", "valueNumber": 1035, "confidence": 0.78, "source": "D(1,7.3320,4.0670,7.9065,4.0608,7.9082,4.2139,7.3337,4.2201)"},
                          "Narrative": {"type": "string", "valueString": "[L190/A104] Review court docket and calendar of pending deadlines for the construction contract", "confidence": 0.863, "source": "D(1,2.2931,4.0615,5.6031,4.0636,5.6030,4.2344,2.2930,4.2323);D(1,2.2889,4.2496,4.4452,4.2509,4.4451,4.4050,2.2888,4.4037)"},
                          "TaskCodeAsBilled": {"type": "string", "valueString": "L190", "confidence": 0.778, "source": "D(1,2.3356,4.0625,2.6332,4.0626,2.6332,4.2317,2.3356,4.2315)"},
                          "ActivityCodeAsBilled": {"type": "string", "valueString": "A104", "confidence": 0.879, "source": "D(1,2.6757,4.0626,2.9817,4.0627,2.9817,4.2319,2.6757,4.2317)"}
                        }
                      }
                    ]
                  },
                  "Expenses": {
                    "type": "array",
                    "valueArray": [
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-07-07", "confidence": 0.808, "source": "D(1,0.5743,8.0825,1.2171,8.0790,1.2171,8.2224,0.5743,8.2227)"},
                          "Description": {"type": "string", "valueString": "[E110] Airfare to Atlanta for deposition", "confidence": 0.725, "source": "D(1,2.2908,8.0744,4.5281,8.0718,4.5283,8.2470,2.2910,8.2496)"},
                          "Quantity": {"type": "number", "valueNumber": 1, "confidence": 0.784, "source": "D(1,6.2878,8.0889,6.3418,8.0889,6.3418,8.2070,6.2878,8.2070)"},
                          "UnitCost": {"type": "number", "valueNumber": 350.71, "confidence": 0.892, "source": "D(1,6.6531,8.0792,7.0349,8.0826,7.0349,8.2115,6.6531,8.2081)"},
                          "Amount": {"type": "number", "valueNumber": 350.71, "confidence": 0.772, "source": "D(1,7.4333,8.0773,7.8944,8.0785,7.8940,8.2190,7.4329,8.2178)"},
                          "ExpenseCodeAsBilled": {"type": "string", "valueString": "E110", "confidence": 0.778, "source": "D(1,2.3338,8.0786,2.6306,8.0779,2.6306,8.2483,2.3338,8.2479)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Date": {"type": "date", "valueDate": "2025-08-07", "confidence": 0.8, "source": "D(1,0.5728,8.3359,1.2181,8.3359,1.2181,8.4756,0.5733,8.4756)"},
                          "Description": {"type": "string", "valueString": "[E115] Court reporter and transcript - deposition", "confidence": 0.719, "source": "D(1,2.2952,8.3279,5.0097,8.3306,5.0095,8.4998,2.2950,8.4971)"},
                          "Quantity": {"type": "number", "valueNumber": 1, "confidence": 0.79, "source": "D(1,6.2837,8.3467,6.3418,8.3467,6.3418,8.4648,6.2837,8.4648)"},
                          "UnitCost": {"type": "number", "valueNumber": 1603.35, "confidence": 0.874, "source": "D(1,6.5535,8.3359,7.0391,8.3359,7.0391,8.4756,6.5535,8.4756)"},
                          "Amount": {"type": "number", "valueNumber": 1603.35, "confidence": 0.733, "source": "D(1,7.3337,8.3304,7.9026,8.3316,7.9023,8.4819,7.3334,8.4807)"},
                          "ExpenseCodeAsBilled": {"type": "string", "valueString": "E115", "confidence": 0.731, "source": "D(1,2.3313,8.3304,2.6261,8.3299,2.6261,8.4959,2.3313,8.4949)"}
                        }
                      }
                    ]
                  },
                  "TimekeeperSummary": {
                    "type": "array",
                    "valueArray": [
                      {
                        "type": "object",
                        "valueObject": {
                          "Name": {"type": "string", "valueString": "Dalia Abernathy", "confidence": 0.726, "source": "D(2,0.5459,2.6586,1.4844,2.6690,1.4826,2.8282,0.5441,2.8178)"},
                          "Title": {"type": "string", "valueString": "Of Counsel", "confidence": 0.892, "source": "D(2,2.9094,2.6608,3.5611,2.6615,3.5610,2.8046,2.9093,2.8039)"},
                          "Hours": {"type": "number", "valueNumber": 2.4, "confidence": 0.596, "source": "D(2,5.0220,2.6636,5.2876,2.6633,5.2876,2.7949,5.0220,2.7952)"},
                          "Rate": {"type": "number", "valueNumber": 780, "confidence": 0.831, "source": "D(2,4.0591,2.6633,4.4451,2.6625,4.4451,2.8010,4.0591,2.8010)"},
                          "Amount": {"type": "number", "valueNumber": 1872, "confidence": 0.787, "source": "D(2,5.8271,2.6596,6.3932,2.6657,6.3916,2.8143,5.8255,2.8082)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Name": {"type": "string", "valueString": "Imani Wickham", "confidence": 0.728, "source": "D(2,0.5432,2.8572,1.4609,2.8574,1.4609,3.0029,0.5432,3.0027)"},
                          "Title": {"type": "string", "valueString": "Associate", "confidence": 0.873, "source": "D(2,2.9053,2.8623,3.4635,2.8605,3.4635,2.9975,2.9053,2.9993)"},
                          "Hours": {"type": "number", "valueNumber": 7.1, "confidence": 0.657, "source": "D(2,5.0303,2.8640,5.2793,2.8572,5.2793,2.9927,5.0303,2.9962)"},
                          "Rate": {"type": "number", "valueNumber": 450, "confidence": 0.897, "source": "D(2,4.0508,2.8572,4.4451,2.8557,4.4451,2.9912,4.0508,2.9917)"},
                          "Amount": {"type": "number", "valueNumber": 3195, "confidence": 0.847, "source": "D(2,5.8229,2.8553,6.3916,2.8550,6.3917,3.0051,5.8230,3.0054)"}
                        }
                      },
                      {
                        "type": "object",
                        "valueObject": {
                          "Name": {"type": "string", "valueString": "Katya Rosenthal", "confidence": 0.726, "source": "D(2,0.5432,3.0476,1.4861,3.0492,1.4858,3.2101,0.5429,3.2085)"},
                          "Title": {"type": "string", "valueString": "Partner", "confidence": 0.862, "source": "D(2,2.9053,3.0588,3.3245,3.0629,3.3245,3.1959,2.9053,3.1935)"},
                          "Hours": {"type": "number", "valueNumber": 3.2, "confidence": 0.619, "source": "D(2,5.0261,3.0569,5.2876,3.0554,5.2876,3.1931,5.0261,3.1931)"},
                          "Rate": {"type": "number", "valueNumber": 865, "confidence": 0.875, "source": "D(2,4.0529,3.0517,4.4451,3.0494,4.4451,3.1891,4.0529,3.1913)"},
                          "Amount": {"type": "number", "valueNumber": 2768, "confidence": 0.847, "source": "D(2,5.8230,3.0504,6.3918,3.0511,6.3916,3.2009,5.8228,3.2002)"}
                        }
                      }
                    ]
                  }
                },
                "analyzerId": "sprk_invoice_v1"
              }
            ]
          }
        }
        """;
}
