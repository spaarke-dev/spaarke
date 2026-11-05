"""poc_check.py - POC harness for the visuals and the library review (local only; never shipped).

  simulate  Write "perfect model" review JSONs from the answer key for kit documents, so the scripts
            can be exercised locally on the real kit files before Cowork runs them:
              - <out>/library/<stem>.review.json + <stem>.clauses.json for every batch NDA;
              - <out>/brightwater/review.json + edit-plan.json (a stand-in plan, one edit per non-GREEN topic).
            --plant-bad-quote alters one quote so the citation check has something to catch.
  score     Compare *.review.json files (e.g. downloaded from Cowork after Act 6) with the answer key:
            accuracy, a confusion matrix and every miss. Writes Markdown to stdout.

Usage (PowerShell; never python in Bash):
  C:\\Python313\\python.exe -B poc_check.py simulate OUT_DIR [--plant-bad-quote]
  C:\\Python313\\python.exe -B poc_check.py score REVIEWS_DIR
"""
import argparse
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
POC = os.path.dirname(HERE)
REPO = os.path.abspath(os.path.join(POC, "..", "..", "..", ".."))
ENGINES = os.path.join(REPO, "src", "accelerators", "core", "engines")
TOPICS = os.path.join(REPO, "src", "accelerators", "build", "out", "nda", "generated", "topics.json")
KEY = os.path.join(POC, "answer-key", "answer-key.json")
BATCH = os.path.join(POC, "kit", "Legal Knowledge", "Inbox", "NDA batch")
MATTER = os.path.join(POC, "kit", "Legal Knowledge", "Matters", "Brightwater Systems", "Brightwater Mutual NDA.docx")
STATUS = {"G": "compliant", "Y": "fallback", "Y2": "deviation", "R": "deviation", "S": "missing", "NC": "deviation"}
ACTION = {"GREEN": "none", "YELLOW": "edit", "RED": "escalate"}
PRIORITY = {"GREEN": None, "YELLOW": "Medium", "RED": "High"}
PROV = {"skill": "nda-library-review", "skill_version": "0.1.2-poc",
        "standards": "Spaarke NDA – Mutual v1.0 (customer playbook)", "run_at": "2026-11-05T12:00:00Z"}


def load(p):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def clauses(docx):
    r = subprocess.run([sys.executable, "-B", os.path.join(ENGINES, "segment_clauses.py"), docx],
                       capture_output=True, text=True, encoding="utf-8")
    if r.returncode:
        raise SystemExit(f"segment_clauses failed on {docx}: {r.stderr}")
    return json.loads(r.stdout)


def locate(topic, sections):
    for phrase in topic.get("look_for", []):
        for s in sections:
            if phrase.lower() in s["text"].lower() and len(s["text"]) > 40:
                return s
    return None


def review_for(name, file_name, key_topics, cats, cl, role):
    out = []
    for tid, k in key_topics.items():
        s = locate(cats[tid], cl["sections"]) if cl else None
        missing = k["variant"] == "S"
        out.append({"topic": tid, "rating": k["rating"], "status": STATUS[k["variant"]],
                    "clause": None if missing or not s else (s.get("number") or s.get("heading")),
                    "quote": None if missing or not s else s["text"][:140].rsplit(" ", 1)[0],
                    "basis": f"Playbook {tid}: {'preferred' if k['rating'] == 'GREEN' else 'see position'}",
                    "note": f"Simulated from the answer key ({role}).",
                    "action": ACTION[k["rating"]], "priority": PRIORITY[k["rating"]]})
    return {"practice": "nda", "document": {"file_name": file_name, "title": name},
            "review_basis": {"playbook": "Spaarke NDA – Mutual", "version": "1.0", "source": "customer",
                             "side": "mutual", "posture": "balanced"},
            "topics": out, "open_questions": [], "provenance": PROV}


def simulate(out, plant):
    key, cats = load(KEY)["documents"], {t["id"]: t for t in load(TOPICS)["topics"]}
    lib = os.path.join(out, "library")
    os.makedirs(lib, exist_ok=True)
    planted = False
    for fn in sorted(os.listdir(BATCH)):
        stem, ext = os.path.splitext(fn)
        cl = clauses(os.path.join(BATCH, fn)) if ext == ".docx" else None
        if cl:
            with open(os.path.join(lib, stem + ".clauses.json"), "w", encoding="utf-8") as f:
                json.dump(cl, f, ensure_ascii=False)
        r = review_for(stem, fn, key[stem]["topics"], cats, cl, key[stem]["role"])
        if plant and not planted and cl:
            for t in r["topics"]:
                if t["quote"]:
                    t["quote"] = "The Receiving Party may disclose Confidential Information to any affiliate without notice"
                    planted = True
                    break
        with open(os.path.join(lib, stem + ".review.json"), "w", encoding="utf-8") as f:
            json.dump(r, f, ensure_ascii=False, indent=1)
    bw = os.path.join(out, "brightwater")
    os.makedirs(bw, exist_ok=True)
    cl = clauses(MATTER)
    r = review_for("Brightwater Mutual NDA", os.path.basename(MATTER), key["Brightwater Mutual NDA"]["topics"], cats, cl, "matter")
    r["provenance"] = dict(PROV, skill="nda-review")
    r["open_questions"] = ["Effective date is blank: confirm the date with the business owner."]
    edits, n = [], 0
    for t in r["topics"]:
        if t["rating"] == "GREEN":
            continue
        n += 1
        edits.append({"para": "stand-in", "find": "stand-in", "replace": "stand-in", "topic": t["topic"],
                      "comment": f"Stand-in edit for {t['topic']}.",
                      "summary": {"n": n, "section": t["clause"] or "-", "bookmark": "NDA_Preamble",
                                  "issue": f"{cats[t['topic']]['title']}: {t['rating']}",
                                  "revision": "Conform to the playbook position.", "priority": t["priority"]}})
    plan = {"author": "Acme Legal", "date": "2026-11-05T12:00:00Z", "provenance": PROV["standards"],
            "review_basis": r["review_basis"], "edits": edits}
    for name, obj in (("review.json", r), ("edit-plan.json", plan)):
        with open(os.path.join(bw, name), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)
    print(json.dumps({"library": lib, "brightwater": bw, "planted_bad_quote": planted}))


def score(folder):
    key = load(KEY)["documents"]
    ratings = ["GREEN", "YELLOW", "RED", "N/A"]
    conf = {a: {b: 0 for b in ratings + ["absent"]} for a in ratings[:3]}
    misses, per_doc, unknown = [], [], []
    for fn in sorted(f for f in os.listdir(folder) if f.endswith(".review.json")):
        r = load(os.path.join(folder, fn))
        stem = os.path.splitext(r.get("document", {}).get("file_name", fn[:-12]))[0]
        if stem not in key or "topics" not in key[stem]:
            unknown.append(stem)
            continue
        got = {t.get("topic"): t.get("rating") for t in r.get("topics", [])}
        ok = 0
        for tid, k in key[stem]["topics"].items():
            g = got.get(tid, "absent")
            conf[k["rating"]][g if g in conf[k["rating"]] else "absent"] += 1
            if g == k["rating"]:
                ok += 1
            else:
                misses.append((stem, tid, k["rating"], g))
        per_doc.append((stem, ok, len(key[stem]["topics"])))
    total = sum(t for _, _, t in per_doc)
    right = sum(o for _, o, _ in per_doc)
    print(f"# Library review score\n\nDocuments scored: {len(per_doc)}; cells: {total}; correct: {right}"
          f" ({100 * right / total:.1f}%)\n" if total else "No scorable documents.\n")
    print("| Expected \\ Got | " + " | ".join(ratings + ["absent"]) + " |\n|---|" + "---|" * (len(ratings) + 1))
    for a in ratings[:3]:
        print(f"| {a} | " + " | ".join(str(conf[a][b]) for b in ratings + ["absent"]) + " |")
    print("\n| Document | Correct |\n|---|---|")
    for s, o, t in per_doc:
        print(f"| {s} | {o}/{t} |")
    if misses:
        print("\n## Misses\n\n| Document | Topic | Expected | Got |\n|---|---|---|---|")
        for m in misses:
            print("| " + " | ".join(m) + " |")
    if unknown:
        print("\nNot in the answer key: " + ", ".join(unknown))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("simulate")
    s.add_argument("out")
    s.add_argument("--plant-bad-quote", action="store_true")
    c = sub.add_parser("score")
    c.add_argument("reviews_dir")
    a = ap.parse_args()
    if a.cmd == "simulate":
        simulate(a.out, a.plant_bad_quote)
    else:
        score(a.reviews_dir)


if __name__ == "__main__":
    main()
