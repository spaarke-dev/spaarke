import{r as p}from"./index-D4H_InIO.js";import{a as k}from"./index-B-lxVbXh.js";import{B as g}from"./BarChart-BOrFFXgZ.js";import"./v4-CtRu48qb.js";import"./Text-C0fEdKO1.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";import"./Legends-DUpH8z2n.js";import"./index-CZVi18Wq.js";import"./index-Dd8bRu6S.js";import"./CartesianChart-L8mYIb7a.js";import"./iframe-KFAmsXlB.js";import"./ChartHoverCard-ScuzKI_J.js";import"./gradients-B-09CRJk.js";const ie={title:"Charts/BarChart",component:g,parameters:{layout:"padded",docs:{description:{component:"BarChart displays categorical data as vertical or horizontal bars. Supports click-to-drill for viewing underlying records."}}},tags:["autodocs"],argTypes:{orientation:{description:"Chart orientation",control:{type:"select"},options:["vertical","horizontal"]},showLabels:{description:"Whether to show data labels on bars",control:{type:"boolean"}},showLegend:{description:"Whether to show the legend",control:{type:"boolean"}},height:{description:"Height of the chart in pixels",control:{type:"number"}},responsive:{description:"Whether the chart should be responsive",control:{type:"boolean"}},title:{description:"Chart title",control:{type:"text"}},drillField:{description:"Field name for drill interaction",control:{type:"text"}}}},e=k("onDrillInteraction"),a=[{label:"Open",value:45,fieldValue:"open"},{label:"In Progress",value:32,fieldValue:"inprogress"},{label:"Pending",value:18,fieldValue:"pending"},{label:"Resolved",value:89,fieldValue:"resolved"},{label:"Closed",value:156,fieldValue:"closed"}],_=[{label:"Jan",value:12500,fieldValue:"2024-01"},{label:"Feb",value:15800,fieldValue:"2024-02"},{label:"Mar",value:18200,fieldValue:"2024-03"},{label:"Apr",value:14300,fieldValue:"2024-04"},{label:"May",value:21500,fieldValue:"2024-05"},{label:"Jun",value:19800,fieldValue:"2024-06"}],q=[{label:"North America",value:245e4,fieldValue:"na"},{label:"Europe",value:189e4,fieldValue:"eu"},{label:"Asia Pacific",value:162e4,fieldValue:"apac"},{label:"Latin America",value:78e4,fieldValue:"latam"},{label:"Middle East",value:45e4,fieldValue:"mea"}],l={args:{data:a,title:"Matters by Status",orientation:"vertical",onDrillInteraction:e,drillField:"statuscode"}},t={args:{data:q,title:"Revenue by Region",orientation:"horizontal",height:350,onDrillInteraction:e,drillField:"region"}},r={args:{data:_,title:"Cases per Month",orientation:"vertical",showLegend:!0,onDrillInteraction:e,drillField:"month"}},n={args:{data:a,title:"Status Distribution",showLegend:!0,onDrillInteraction:e,drillField:"statuscode"}},o={args:{data:[{label:"Critical",value:12,fieldValue:"critical",color:"#D13438"},{label:"High",value:28,fieldValue:"high",color:"#FF8C00"},{label:"Medium",value:45,fieldValue:"medium",color:"#FFB900"},{label:"Low",value:67,fieldValue:"low",color:"#107C10"}],title:"Issues by Priority",onDrillInteraction:e,drillField:"priority"}},i={args:{data:_,title:"Monthly Overview (View Only)",orientation:"vertical"}},s={args:{data:a.slice(0,3),title:"Top 3 Status",height:200,onDrillInteraction:e,drillField:"statuscode"}},d={args:{data:[],title:"No Data Available"}},c={args:{data:[{label:"Category A",value:150,fieldValue:"a"},{label:"Category B",value:230,fieldValue:"b"},{label:"Category C",value:180,fieldValue:"c"},{label:"Category D",value:290,fieldValue:"d"},{label:"Category E",value:120,fieldValue:"e"},{label:"Category F",value:350,fieldValue:"f"},{label:"Category G",value:200,fieldValue:"g"},{label:"Category H",value:270,fieldValue:"h"},{label:"Category I",value:160,fieldValue:"i"},{label:"Category J",value:310,fieldValue:"j"}],title:"10 Categories",height:400,onDrillInteraction:e,drillField:"category"}},u={render:()=>p.createElement("div",{style:{display:"grid",gridTemplateColumns:"1fr 1fr",gap:"24px"}},p.createElement(g,{data:a,title:"Vertical",orientation:"vertical",onDrillInteraction:e,drillField:"status"}),p.createElement(g,{data:a,title:"Horizontal",orientation:"horizontal",onDrillInteraction:e,drillField:"status"}))};var m,h,v;l.parameters={...l.parameters,docs:{...(m=l.parameters)==null?void 0:m.docs,source:{originalSource:`{
  args: {
    data: statusData,
    title: "Matters by Status",
    orientation: "vertical",
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(v=(h=l.parameters)==null?void 0:h.docs)==null?void 0:v.source}}};var b,f,y;t.parameters={...t.parameters,docs:{...(b=t.parameters)==null?void 0:b.docs,source:{originalSource:`{
  args: {
    data: regionData,
    title: "Revenue by Region",
    orientation: "horizontal",
    height: 350,
    onDrillInteraction: handleDrill,
    drillField: "region"
  }
}`,...(y=(f=t.parameters)==null?void 0:f.docs)==null?void 0:y.source}}};var D,C,V;r.parameters={...r.parameters,docs:{...(D=r.parameters)==null?void 0:D.docs,source:{originalSource:`{
  args: {
    data: monthlyData,
    title: "Cases per Month",
    orientation: "vertical",
    showLegend: true,
    onDrillInteraction: handleDrill,
    drillField: "month"
  }
}`,...(V=(C=r.parameters)==null?void 0:C.docs)==null?void 0:V.source}}};var F,I,w;n.parameters={...n.parameters,docs:{...(F=n.parameters)==null?void 0:F.docs,source:{originalSource:`{
  args: {
    data: statusData,
    title: "Status Distribution",
    showLegend: true,
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(w=(I=n.parameters)==null?void 0:I.docs)==null?void 0:w.source}}};var S,L,M;o.parameters={...o.parameters,docs:{...(S=o.parameters)==null?void 0:S.docs,source:{originalSource:`{
  args: {
    data: [{
      label: "Critical",
      value: 12,
      fieldValue: "critical",
      color: "#D13438"
    }, {
      label: "High",
      value: 28,
      fieldValue: "high",
      color: "#FF8C00"
    }, {
      label: "Medium",
      value: 45,
      fieldValue: "medium",
      color: "#FFB900"
    }, {
      label: "Low",
      value: 67,
      fieldValue: "low",
      color: "#107C10"
    }],
    title: "Issues by Priority",
    onDrillInteraction: handleDrill,
    drillField: "priority"
  }
}`,...(M=(L=o.parameters)==null?void 0:L.docs)==null?void 0:M.source}}};var E,z,B;i.parameters={...i.parameters,docs:{...(E=i.parameters)==null?void 0:E.docs,source:{originalSource:`{
  args: {
    data: monthlyData,
    title: "Monthly Overview (View Only)",
    orientation: "vertical"
  }
}`,...(B=(z=i.parameters)==null?void 0:z.docs)==null?void 0:B.source}}};var H,x,A;s.parameters={...s.parameters,docs:{...(H=s.parameters)==null?void 0:H.docs,source:{originalSource:`{
  args: {
    data: statusData.slice(0, 3),
    title: "Top 3 Status",
    height: 200,
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(A=(x=s.parameters)==null?void 0:x.docs)==null?void 0:A.source}}};var T,O,N;d.parameters={...d.parameters,docs:{...(T=d.parameters)==null?void 0:T.docs,source:{originalSource:`{
  args: {
    data: [],
    title: "No Data Available"
  }
}`,...(N=(O=d.parameters)==null?void 0:O.docs)==null?void 0:N.source}}};var P,R,W;c.parameters={...c.parameters,docs:{...(P=c.parameters)==null?void 0:P.docs,source:{originalSource:`{
  args: {
    data: [{
      label: "Category A",
      value: 150,
      fieldValue: "a"
    }, {
      label: "Category B",
      value: 230,
      fieldValue: "b"
    }, {
      label: "Category C",
      value: 180,
      fieldValue: "c"
    }, {
      label: "Category D",
      value: 290,
      fieldValue: "d"
    }, {
      label: "Category E",
      value: 120,
      fieldValue: "e"
    }, {
      label: "Category F",
      value: 350,
      fieldValue: "f"
    }, {
      label: "Category G",
      value: 200,
      fieldValue: "g"
    }, {
      label: "Category H",
      value: 270,
      fieldValue: "h"
    }, {
      label: "Category I",
      value: 160,
      fieldValue: "i"
    }, {
      label: "Category J",
      value: 310,
      fieldValue: "j"
    }],
    title: "10 Categories",
    height: 400,
    onDrillInteraction: handleDrill,
    drillField: "category"
  }
}`,...(W=(R=c.parameters)==null?void 0:R.docs)==null?void 0:W.source}}};var J,j,G;u.parameters={...u.parameters,docs:{...(J=u.parameters)==null?void 0:J.docs,source:{originalSource:`{
  render: () => <div style={{
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: "24px"
  }}>\r
      <BarChart data={statusData} title="Vertical" orientation="vertical" onDrillInteraction={handleDrill} drillField="status" />\r
      <BarChart data={statusData} title="Horizontal" orientation="horizontal" onDrillInteraction={handleDrill} drillField="status" />\r
    </div>
}`,...(G=(j=u.parameters)==null?void 0:j.docs)==null?void 0:G.source}}};const se=["Default","Horizontal","MonthlyTrend","WithLegend","CustomColors","NonInteractive","Compact","EmptyData","LargeDataset","Comparison"];export{s as Compact,u as Comparison,o as CustomColors,l as Default,d as EmptyData,t as Horizontal,c as LargeDataset,r as MonthlyTrend,i as NonInteractive,n as WithLegend,se as __namedExportsOrder,ie as default};
