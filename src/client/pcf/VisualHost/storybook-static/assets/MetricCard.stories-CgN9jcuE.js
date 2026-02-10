import{r as u}from"./index-D4H_InIO.js";import{a as j}from"./index-B-lxVbXh.js";import{M as g}from"./MetricCard-CkD2OPWL.js";import"./v4-CtRu48qb.js";import"./Text-C0fEdKO1.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";import"./Card-8YjWmAfr.js";import"./constants-C-FBwxq0.js";import"./useIsomorphicLayoutEffect-DxaFQ3i0.js";import"./keys-BStMjYrg.js";import"./useFocusWithin-DRp-habq.js";import"./__resetStyles.esm-CYXmtSuc.js";import"./chunk-21-Bt5ZjQBf.js";import"./createFluentIcon-D98y1lfk.js";import"./IconDirectionContext-Dwe_X4OL.js";const se={title:"Charts/MetricCard",component:g,parameters:{layout:"centered",docs:{description:{component:"MetricCard displays a single aggregate value with optional trend indicator. Supports click-to-drill for viewing underlying records."}}},tags:["autodocs"],argTypes:{value:{description:"The main metric value to display",control:{type:"text"}},label:{description:"Label describing what the metric represents",control:{type:"text"}},description:{description:"Optional description or subtitle",control:{type:"text"}},trend:{description:"Trend direction (up = positive, down = negative)",control:{type:"select"},options:["up","down","neutral",void 0]},trendValue:{description:"Percentage change for trend display",control:{type:"number"}},interactive:{description:"Whether the card should be interactive",control:{type:"boolean"}},compact:{description:"Compact mode for smaller displays",control:{type:"boolean"}},drillField:{description:"Field name for drill interaction",control:{type:"text"}},drillValue:{description:"Value to filter by when drilling",control:{type:"text"}}}},e=j("onDrillInteraction"),r={args:{value:1234,label:"Total Records",onDrillInteraction:e,drillField:"total",drillValue:"all"}},l={args:{value:45,label:"Open Matters",trend:"up",trendValue:12.5,onDrillInteraction:e,drillField:"statuscode",drillValue:1}},a={args:{value:128,label:"Pending Tasks",trend:"down",trendValue:-8.3,onDrillInteraction:e,drillField:"statuscode",drillValue:2}},n={args:{value:125e4,label:"Total Revenue",description:"Year to date",trend:"up",trendValue:15.2,onDrillInteraction:e,drillField:"revenue",drillValue:"ytd"}},t={args:{value:"$1.2M",label:"Revenue",description:"This quarter",trend:"up",trendValue:8.7,onDrillInteraction:e,drillField:"quarter",drillValue:"Q4"}},i={args:{value:42,label:"Active Users",compact:!0,onDrillInteraction:e,drillField:"active",drillValue:!0}},o={args:{value:99.5,label:"Uptime %",description:"Last 30 days",interactive:!1}},d={args:{value:87,label:"Cases Resolved",description:"This week (15% above target)",trend:"up",trendValue:15,onDrillInteraction:e,drillField:"resolved",drillValue:!0}},s={render:()=>{const p=[{value:1250,label:"Total Accounts",trend:"up",trendValue:5.2,drillField:"account",drillValue:"all"},{value:45,label:"Open Opportunities",trend:"down",trendValue:-3.1,drillField:"opportunity",drillValue:"open"},{value:"$2.4M",label:"Pipeline Value",trend:"up",trendValue:12.8,drillField:"pipeline",drillValue:"active"},{value:89,label:"Win Rate %",description:"Last quarter",drillField:"winrate",drillValue:"q4"}];return u.createElement("div",{style:{display:"grid",gridTemplateColumns:"repeat(2, 1fr)",gap:"16px",padding:"16px"}},p.map((m,v)=>u.createElement(g,{key:v,...m,onDrillInteraction:e})))}},c={render:()=>{const p=[{value:142,label:"New",compact:!0,drillField:"status",drillValue:"new"},{value:87,label:"In Progress",compact:!0,drillField:"status",drillValue:"inprogress"},{value:234,label:"Resolved",compact:!0,drillField:"status",drillValue:"resolved"},{value:12,label:"Escalated",compact:!0,drillField:"status",drillValue:"escalated"}];return u.createElement("div",{style:{display:"flex",gap:"12px",padding:"16px",flexWrap:"wrap"}},p.map((m,v)=>u.createElement(g,{key:v,...m,onDrillInteraction:e})))}};var V,b,y;r.parameters={...r.parameters,docs:{...(V=r.parameters)==null?void 0:V.docs,source:{originalSource:`{
  args: {
    value: 1234,
    label: "Total Records",
    onDrillInteraction: handleDrill,
    drillField: "total",
    drillValue: "all"
  }
}`,...(y=(b=r.parameters)==null?void 0:b.docs)==null?void 0:y.source}}};var D,F,h;l.parameters={...l.parameters,docs:{...(D=l.parameters)==null?void 0:D.docs,source:{originalSource:`{
  args: {
    value: 45,
    label: "Open Matters",
    trend: "up",
    trendValue: 12.5,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
    drillValue: 1
  }
}`,...(h=(F=l.parameters)==null?void 0:F.docs)==null?void 0:h.source}}};var I,w,x;a.parameters={...a.parameters,docs:{...(I=a.parameters)==null?void 0:I.docs,source:{originalSource:`{
  args: {
    value: 128,
    label: "Pending Tasks",
    trend: "down",
    trendValue: -8.3,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
    drillValue: 2
  }
}`,...(x=(w=a.parameters)==null?void 0:w.docs)==null?void 0:x.source}}};var T,f,C;n.parameters={...n.parameters,docs:{...(T=n.parameters)==null?void 0:T.docs,source:{originalSource:`{
  args: {
    value: 1250000,
    label: "Total Revenue",
    description: "Year to date",
    trend: "up",
    trendValue: 15.2,
    onDrillInteraction: handleDrill,
    drillField: "revenue",
    drillValue: "ytd"
  }
}`,...(C=(f=n.parameters)==null?void 0:f.docs)==null?void 0:C.source}}};var M,R,S;t.parameters={...t.parameters,docs:{...(M=t.parameters)==null?void 0:M.docs,source:{originalSource:`{
  args: {
    value: "$1.2M",
    label: "Revenue",
    description: "This quarter",
    trend: "up",
    trendValue: 8.7,
    onDrillInteraction: handleDrill,
    drillField: "quarter",
    drillValue: "Q4"
  }
}`,...(S=(R=t.parameters)==null?void 0:R.docs)==null?void 0:S.source}}};var k,P,q;i.parameters={...i.parameters,docs:{...(k=i.parameters)==null?void 0:k.docs,source:{originalSource:`{
  args: {
    value: 42,
    label: "Active Users",
    compact: true,
    onDrillInteraction: handleDrill,
    drillField: "active",
    drillValue: true
  }
}`,...(q=(P=i.parameters)==null?void 0:P.docs)==null?void 0:q.source}}};var E,O,L;o.parameters={...o.parameters,docs:{...(E=o.parameters)==null?void 0:E.docs,source:{originalSource:`{
  args: {
    value: 99.5,
    label: "Uptime %",
    description: "Last 30 days",
    interactive: false
  }
}`,...(L=(O=o.parameters)==null?void 0:O.docs)==null?void 0:L.source}}};var W,N,U;d.parameters={...d.parameters,docs:{...(W=d.parameters)==null?void 0:W.docs,source:{originalSource:`{
  args: {
    value: 87,
    label: "Cases Resolved",
    description: "This week (15% above target)",
    trend: "up",
    trendValue: 15,
    onDrillInteraction: handleDrill,
    drillField: "resolved",
    drillValue: true
  }
}`,...(U=(N=d.parameters)==null?void 0:N.docs)==null?void 0:U.source}}};var A,$,G;s.parameters={...s.parameters,docs:{...(A=s.parameters)==null?void 0:A.docs,source:{originalSource:`{
  render: () => {
    const metrics: IMetricCardProps[] = [{
      value: 1250,
      label: "Total Accounts",
      trend: "up",
      trendValue: 5.2,
      drillField: "account",
      drillValue: "all"
    }, {
      value: 45,
      label: "Open Opportunities",
      trend: "down",
      trendValue: -3.1,
      drillField: "opportunity",
      drillValue: "open"
    }, {
      value: "$2.4M",
      label: "Pipeline Value",
      trend: "up",
      trendValue: 12.8,
      drillField: "pipeline",
      drillValue: "active"
    }, {
      value: 89,
      label: "Win Rate %",
      description: "Last quarter",
      drillField: "winrate",
      drillValue: "q4"
    }];
    return <div style={{
      display: "grid",
      gridTemplateColumns: "repeat(2, 1fr)",
      gap: "16px",
      padding: "16px"
    }}>\r
        {metrics.map((metric, index) => <MetricCard key={index} {...metric} onDrillInteraction={handleDrill} />)}\r
      </div>;
  }
}`,...(G=($=s.parameters)==null?void 0:$.docs)==null?void 0:G.source}}};var Q,Y,_;c.parameters={...c.parameters,docs:{...(Q=c.parameters)==null?void 0:Q.docs,source:{originalSource:`{
  render: () => {
    const metrics: IMetricCardProps[] = [{
      value: 142,
      label: "New",
      compact: true,
      drillField: "status",
      drillValue: "new"
    }, {
      value: 87,
      label: "In Progress",
      compact: true,
      drillField: "status",
      drillValue: "inprogress"
    }, {
      value: 234,
      label: "Resolved",
      compact: true,
      drillField: "status",
      drillValue: "resolved"
    }, {
      value: 12,
      label: "Escalated",
      compact: true,
      drillField: "status",
      drillValue: "escalated"
    }];
    return <div style={{
      display: "flex",
      gap: "12px",
      padding: "16px",
      flexWrap: "wrap"
    }}>\r
        {metrics.map((metric, index) => <MetricCard key={index} {...metric} onDrillInteraction={handleDrill} />)}\r
      </div>;
  }
}`,...(_=(Y=c.parameters)==null?void 0:Y.docs)==null?void 0:_.source}}};const ce=["Default","TrendUp","TrendDown","LargeNumber","CurrencyValue","Compact","NonInteractive","WithDescription","MetricGrid","DashboardRow"];export{i as Compact,t as CurrencyValue,c as DashboardRow,r as Default,n as LargeNumber,s as MetricGrid,o as NonInteractive,a as TrendDown,l as TrendUp,d as WithDescription,ce as __namedExportsOrder,se as default};
