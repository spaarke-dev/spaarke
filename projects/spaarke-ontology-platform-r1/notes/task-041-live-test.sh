#!/bin/bash
# Header: replayable from notes/. Needs `az login` as a dev operator holding ActOnBehalfOfAnotherUser; caller id below is a Spaarke-roles-only human.
# Task 041 live test, spaarkedev1 only. All rows prefixed zz-041-; cleanup in EXIT trap.
cd "$(mktemp -d)"
TOK=$(az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv)
B=https://spaarkedev1.crm.dynamics.com/api/data/v9.2
CH=c46d44ca-33bd-f111-aaaf-0022482913fc
admin() { curl -s -w '\nHTTP %{http_code}\n' -H "Authorization: Bearer $TOK" -H "Accept: application/json" -H "Content-Type: application/json" -H "OData-Version: 4.0" "$@"; }
as() { curl -s -w '\nHTTP %{http_code}\n' -H "Authorization: Bearer $TOK" -H "MSCRMCallerID: $CH" -H "Accept: application/json" -H "Content-Type: application/json" -H "OData-Version: 4.0" "$@"; }
cleanup() {
  echo; echo "=== CLEANUP (EXIT trap) ==="
  for t in sprk_signals:sprk_signalid:sprk_name sprk_decisionrecords:sprk_decisionrecordid:sprk_name; do
    IFS=: read set id nm <<<"$t"
    ids=$(curl -s -H "Authorization: Bearer $TOK" -H "Accept: application/json" "$B/$set?\$select=$id&\$filter=startswith($nm,'zz-041-')" | python -c "import json,sys;print(' '.join(r['$id'] for r in json.load(sys.stdin).get('value',[])))")
    for i in $ids; do echo -n "delete $set $i -> "; curl -s -o /dev/null -w '%{http_code}\n' -X DELETE -H "Authorization: Bearer $TOK" "$B/$set($i)"; done
  done
}
trap cleanup EXIT
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)
echo "=== run $NOW  caller=Chelsea Friez $CH ==="

echo; echo "--- 1. Chelsea creates a DR (her own probe row)"
cat > dr.json <<J
{"sprk_name":"zz-041-dr-probe","sprk_decidedon":"$NOW","sprk_decisionoutcome":100000000,"sprk_factsnapshot":"{}","sprk_proposedaction":"zz-041 probe","sprk_recordclass":100000000}
J
as -D hdr.txt -X POST "$B/sprk_decisionrecords" -d @dr.json | tee create.out
DR=$(grep -i '^OData-EntityId' hdr.txt | sed -E 's/.*\(([0-9a-f-]{36})\).*/\1/')
echo "DR=$DR"
[ -z "$DR" ] && { echo "DR create failed; abort"; exit 1; }

echo; echo "--- 2. UPDATE as Chelsea (expect 403)"
as -X PATCH "$B/sprk_decisionrecords($DR)" -d '{"sprk_proposedaction":"zz-041 tampered"}'
echo; echo "--- 3. DELETE as Chelsea (expect 403)"
as -X DELETE "$B/sprk_decisionrecords($DR)"
echo; echo "--- 4a. DEACTIVATE as Chelsea: PATCH statecode=1 (expect 403)"
as -X PATCH "$B/sprk_decisionrecords($DR)" -d '{"statecode":1,"statuscode":2}'
echo; echo "--- 5. RetrievePrincipalAccess for Chelsea on the row (she is its owner)"
admin "$B/systemusers($CH)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)?@t=%7B%22%40odata.id%22%3A%22sprk_decisionrecords($DR)%22%7D"
echo; echo "--- 6. Row unchanged after the attempts (read as Chelsea)"
as "$B/sprk_decisionrecords($DR)?\$select=sprk_name,sprk_proposedaction,statecode,statuscode,_ownerid_value"
echo; echo "--- 7. Positive control: admin PATCH (expect 204, so the 403s are the privilege check)"
admin -X PATCH "$B/sprk_decisionrecords($DR)" -d '{"sprk_proposedaction":"zz-041 admin edit"}'

echo; echo "--- 8. One decision, three Signals (admin creates; FK is on the Signal)"
for n in 1 2 3; do
cat > sig$n.json <<J
{"sprk_name":"zz-041-sig-$n","sprk_dedupekey":"zz-041|test|$n|$RANDOM","sprk_evidencerefs":"[]","sprk_factsnapshot":"{}","sprk_firstdetected":"$NOW","sprk_lane":100000000,"sprk_lastevaluated":"$NOW","sprk_policycode":"zz-041","sprk_sentence":"zz-041 test","sprk_signalstatus":100000002,"sprk_DecisionRecord@odata.bind":"/sprk_decisionrecords($DR)"}
J
admin -X POST "$B/sprk_signals" -d @sig$n.json | tail -c 400
done
# decoy not linked to the decision
cat > sig4.json <<J
{"sprk_name":"zz-041-sig-4-unlinked","sprk_dedupekey":"zz-041|test|4|$RANDOM","sprk_evidencerefs":"[]","sprk_factsnapshot":"{}","sprk_firstdetected":"$NOW","sprk_lane":100000000,"sprk_lastevaluated":"$NOW","sprk_policycode":"zz-041","sprk_sentence":"zz-041 test","sprk_signalstatus":100000000}
J
admin -X POST "$B/sprk_signals" -d @sig4.json | tail -c 300

echo; echo "--- 9. Subgrid read as Chelsea: relationship navigation from the DR side (sprk_signal_decisionrecord)"
as "$B/sprk_decisionrecords($DR)/sprk_signal_decisionrecord?\$select=sprk_name,_sprk_decisionrecord_value&\$orderby=sprk_name"
echo; echo "--- 10. Same read as FetchXML (what a subgrid executes), as Chelsea"
FX="<fetch><entity name='sprk_signal'><attribute name='sprk_name'/><filter><condition attribute='sprk_decisionrecord' operator='eq' value='$DR'/></filter><order attribute='sprk_name'/></entity></fetch>"
as -G "$B/sprk_signals" --data-urlencode "fetchXml=$FX"
echo; echo "--- 11. DR-side query: no column on the DR can list its signals; DR row carries no signal reference"
as "$B/sprk_decisionrecords($DR)?\$select=sprk_name" | head -c 600
