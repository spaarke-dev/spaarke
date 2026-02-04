import{a as b}from"./index-B-lxVbXh.js";import{M as S}from"./MiniTable-Bjy0CQL5.js";import"./v4-CtRu48qb.js";import"./index-D4H_InIO.js";import"./Text-C0fEdKO1.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";import"./useFocusVisible-s19g94sk.js";import"./constants-C-FBwxq0.js";import"./useFocusWithin-DRp-habq.js";import"./useARIAButtonProps-CqzD7EOT.js";import"./useIsomorphicLayoutEffect-DxaFQ3i0.js";import"./keys-BStMjYrg.js";import"./chunk-21-Bt5ZjQBf.js";import"./createFluentIcon-D98y1lfk.js";import"./IconDirectionContext-Dwe_X4OL.js";const j={title:"Charts/MiniTable",component:S,parameters:{layout:"padded",docs:{description:{component:"MiniTable displays a compact ranked table with drill-through support."}}},tags:["autodocs"]},i=b("onDrillInteraction"),e=[{key:"name",header:"Client"},{key:"value",header:"Revenue",isValue:!0}],s=[{id:"1",values:{name:"Acme Corp",value:"$2.4M"},fieldValue:"acme-001"},{id:"2",values:{name:"TechStart Inc",value:"$1.8M"},fieldValue:"tech-002"},{id:"3",values:{name:"Global Partners",value:"$1.5M"},fieldValue:"global-003"},{id:"4",values:{name:"Innovation Labs",value:"$1.2M"},fieldValue:"innov-004"},{id:"5",values:{name:"Summit Group",value:"$980K"},fieldValue:"summit-005"},{id:"6",values:{name:"BlueSky Co",value:"$850K"},fieldValue:"blue-006"},{id:"7",values:{name:"Apex Solutions",value:"$720K"},fieldValue:"apex-007"}],a={args:{items:s,columns:e,title:"Top 5 Clients by Revenue",topN:5,onDrillInteraction:i,drillField:"accountid"}},n={args:{items:s,columns:e,title:"Top 10 Clients",topN:10,onDrillInteraction:i,drillField:"accountid"}},t={args:{items:s,columns:e,title:"Recent Clients",showRank:!1,onDrillInteraction:i,drillField:"accountid"}},r={args:{items:[{id:"1",values:{matter:"Smith vs Jones",hours:"156",amount:"$45,200"},fieldValue:"m-001"},{id:"2",values:{matter:"Tech Corp IP Case",hours:"142",amount:"$41,800"},fieldValue:"m-002"},{id:"3",values:{matter:"Estate Planning - Davis",hours:"98",amount:"$28,500"},fieldValue:"m-003"},{id:"4",values:{matter:"Contract Review - ABC",hours:"87",amount:"$25,100"},fieldValue:"m-004"},{id:"5",values:{matter:"M&A Advisory",hours:"76",amount:"$22,800"},fieldValue:"m-005"}],columns:[{key:"matter",header:"Matter",width:"200px"},{key:"hours",header:"Hours",isValue:!0},{key:"amount",header:"Amount",isValue:!0}],title:"Top Matters by Hours",onDrillInteraction:i,drillField:"matterid"}},o={args:{items:s,columns:e,title:"View Only",interactive:!1}},l={args:{items:[],columns:e,title:"No Data"}};var u,m,d;a.parameters={...a.parameters,docs:{...(u=a.parameters)==null?void 0:u.docs,source:{originalSource:`{
  args: {
    items,
    columns,
    title: "Top 5 Clients by Revenue",
    topN: 5,
    onDrillInteraction: handleDrill,
    drillField: "accountid"
  }
}`,...(d=(m=a.parameters)==null?void 0:m.docs)==null?void 0:d.source}}};var c,p,v;n.parameters={...n.parameters,docs:{...(c=n.parameters)==null?void 0:c.docs,source:{originalSource:`{
  args: {
    items,
    columns,
    title: "Top 10 Clients",
    topN: 10,
    onDrillInteraction: handleDrill,
    drillField: "accountid"
  }
}`,...(v=(p=n.parameters)==null?void 0:p.docs)==null?void 0:v.source}}};var h,f,V;t.parameters={...t.parameters,docs:{...(h=t.parameters)==null?void 0:h.docs,source:{originalSource:`{
  args: {
    items,
    columns,
    title: "Recent Clients",
    showRank: false,
    onDrillInteraction: handleDrill,
    drillField: "accountid"
  }
}`,...(V=(f=t.parameters)==null?void 0:f.docs)==null?void 0:V.source}}};var g,D,y;r.parameters={...r.parameters,docs:{...(g=r.parameters)==null?void 0:g.docs,source:{originalSource:`{
  args: {
    items: [{
      id: "1",
      values: {
        matter: "Smith vs Jones",
        hours: "156",
        amount: "$45,200"
      },
      fieldValue: "m-001"
    }, {
      id: "2",
      values: {
        matter: "Tech Corp IP Case",
        hours: "142",
        amount: "$41,800"
      },
      fieldValue: "m-002"
    }, {
      id: "3",
      values: {
        matter: "Estate Planning - Davis",
        hours: "98",
        amount: "$28,500"
      },
      fieldValue: "m-003"
    }, {
      id: "4",
      values: {
        matter: "Contract Review - ABC",
        hours: "87",
        amount: "$25,100"
      },
      fieldValue: "m-004"
    }, {
      id: "5",
      values: {
        matter: "M&A Advisory",
        hours: "76",
        amount: "$22,800"
      },
      fieldValue: "m-005"
    }],
    columns: [{
      key: "matter",
      header: "Matter",
      width: "200px"
    }, {
      key: "hours",
      header: "Hours",
      isValue: true
    }, {
      key: "amount",
      header: "Amount",
      isValue: true
    }],
    title: "Top Matters by Hours",
    onDrillInteraction: handleDrill,
    drillField: "matterid"
  }
}`,...(y=(D=r.parameters)==null?void 0:D.docs)==null?void 0:y.source}}};var C,$,M;o.parameters={...o.parameters,docs:{...(C=o.parameters)==null?void 0:C.docs,source:{originalSource:`{
  args: {
    items,
    columns,
    title: "View Only",
    interactive: false
  }
}`,...(M=($=o.parameters)==null?void 0:$.docs)==null?void 0:M.source}}};var I,k,T;l.parameters={...l.parameters,docs:{...(I=l.parameters)==null?void 0:I.docs,source:{originalSource:`{
  args: {
    items: [],
    columns,
    title: "No Data"
  }
}`,...(T=(k=l.parameters)==null?void 0:k.docs)==null?void 0:T.source}}};const q=["Default","Top10","NoRank","MultipleColumns","NonInteractive","EmptyData"];export{a as Default,l as EmptyData,r as MultipleColumns,t as NoRank,o as NonInteractive,n as Top10,q as __namedExportsOrder,j as default};
