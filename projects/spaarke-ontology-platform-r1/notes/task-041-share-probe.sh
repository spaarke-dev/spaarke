#!/bin/bash
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
echo "=== share-rights run $NOW caller=Chelsea Friez $CH ==="
cat > dr2.json <<J
{"sprk_name":"zz-041-dr-share-probe","sprk_decidedon":"$NOW","sprk_decisionoutcome":100000000,"sprk_factsnapshot":"{}","sprk_proposedaction":"zz-041 share probe","sprk_recordclass":100000000}
J
echo "--- A. admin creates a DR owned by admin (not Chelsea)"
admin -D hdr2.txt -X POST "$B/sprk_decisionrecords" -d @dr2.json
DR=$(grep -i '^OData-EntityId' hdr2.txt | grep -oE '[0-9a-f]{8}-[0-9a-f-]{27}'); echo "DR=$DR"
[ -z "$DR" ] && exit 1
echo "--- B. admin shares the row with Chelsea: Read+Write+Delete+Append+AppendTo (what uac-r2 mirrored shares can carry)"
admin -X POST "$B/GrantAccess" -d "{\"Target\":{\"@odata.type\":\"Microsoft.Dynamics.CRM.sprk_decisionrecord\",\"sprk_decisionrecordid\":\"$DR\"},\"PrincipalAccess\":{\"Principal\":{\"@odata.type\":\"Microsoft.Dynamics.CRM.systemuser\",\"systemuserid\":\"$CH\"},\"AccessMask\":\"ReadAccess,WriteAccess,DeleteAccess,AppendAccess,AppendToAccess\"}}"
echo "--- C. RetrievePrincipalAccess for Chelsea on that row"
admin "$B/systemusers($CH)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)?@t=%7B%22%40odata.id%22%3A%22sprk_decisionrecords($DR)%22%7D"
echo "--- D. Chelsea reads the shared row (expect 200)"
as "$B/sprk_decisionrecords($DR)?\$select=sprk_name,_ownerid_value"
echo "--- E. Chelsea UPDATE the shared row (expect 403)"
as -X PATCH "$B/sprk_decisionrecords($DR)" -d '{"sprk_proposedaction":"zz-041 tampered via share"}'
echo "--- F. Chelsea DELETE the shared row (expect 403)"
as -X DELETE "$B/sprk_decisionrecords($DR)"
echo "--- G. Chelsea deactivate the shared row (expect 403)"
as -X PATCH "$B/sprk_decisionrecords($DR)" -d '{"statecode":1,"statuscode":2}'
