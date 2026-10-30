"""Create the spike-only analyzers (task 005). Create-only unless --replace; never touches sprk_invoice_v1.

usage: python deploy_spike_analyzers.py NAME [NAME...] [--replace]
NAME -> analyzers/NAME.json; api-version: preview for *_p / *v1p (spike only, D-53), GA otherwise.
"""
import json
import sys
import time

from common import CU_ENDPOINT, GA, PREVIEW, HERE, append_run, http, utcnow

API = {"sprk_invoice_v1p": PREVIEW, "sprk_invoice_router_p": PREVIEW,
       "sprk_invoice_v1_cls": GA, "sprk_invoice_router": GA}


def main():
    names = [a for a in sys.argv[1:] if not a.startswith("--")]
    replace = "--replace" in sys.argv
    for name in names:
        assert name != "sprk_invoice_v1" and name in API
        body = json.loads((HERE / "analyzers" / f"{name}.json").read_text(encoding="utf-8"))
        api = API[name]
        url = f"{CU_ENDPOINT}/contentunderstanding/analyzers/{name}?api-version={api}" + ("&allowReplace=true" if replace else "")
        st, hdrs, res = http("PUT", url, body)
        rec = {"id": name, "arm": "analyzer-create", "api": api, "put_status": st, "at": utcnow()}
        if st not in (200, 201):
            rec["error"] = json.dumps(res)[:600]
        else:
            op = hdrs.get("Operation-Location") or hdrs.get("operation-location")
            status = None
            for _ in range(120):
                if not op:
                    break
                time.sleep(2)
                _, _, r = http("GET", op)
                status = (r or {}).get("status")
                if status in ("Succeeded", "Failed", "Canceled"):
                    if status != "Succeeded":
                        rec["error"] = json.dumps(r)[:600]
                    break
            rec["op_status"] = status
            st3, _, got = http("GET", f"{CU_ENDPOINT}/contentunderstanding/analyzers/{name}?api-version={api}")
            rec["get_status"] = st3
            rec["workflow"] = ((got or {}).get("config") or {}).get("workflow") if isinstance(got, dict) else None
            rec["warnings"] = (got or {}).get("warnings") if isinstance(got, dict) else None
        append_run("analyzers", rec)
        print(json.dumps(rec))


if __name__ == "__main__":
    main()
