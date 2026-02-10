import{r as a}from"./index-D4H_InIO.js";import{a as u}from"./index-B-lxVbXh.js";import{V as e,A as O}from"./index-DdRfOkZ2.js";import{M as ke}from"./MetricCard-CkD2OPWL.js";import{B as ve}from"./BarChart-BOrFFXgZ.js";import{L as _e}from"./LineChart-Y5rhgTmV.js";import{D as we}from"./DonutChart-BEPSkMiT.js";import{S as Te}from"./StatusDistributionBar-Cd8i7Oe1.js";import{C as Ve}from"./CalendarVisual-Y3fEVNV0.js";import{M as Se}from"./MiniTable-Bjy0CQL5.js";import{m as Ie,T as f,t as m}from"./Text-C0fEdKO1.js";import"./v4-CtRu48qb.js";import"./Card-8YjWmAfr.js";import"./constants-C-FBwxq0.js";import"./useIsomorphicLayoutEffect-DxaFQ3i0.js";import"./keys-BStMjYrg.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";import"./useFocusWithin-DRp-habq.js";import"./__resetStyles.esm-CYXmtSuc.js";import"./chunk-21-Bt5ZjQBf.js";import"./createFluentIcon-D98y1lfk.js";import"./IconDirectionContext-Dwe_X4OL.js";import"./Legends-DUpH8z2n.js";import"./index-CZVi18Wq.js";import"./index-Dd8bRu6S.js";import"./CartesianChart-L8mYIb7a.js";import"./iframe-KFAmsXlB.js";import"./ChartHoverCard-ScuzKI_J.js";import"./gradients-B-09CRJk.js";import"./colors-CW2ns9F7.js";import"./useARIAButtonProps-CqzD7EOT.js";import"./useFocusVisible-s19g94sk.js";const Be=Ie({container:{display:"flex",alignItems:"center",justifyContent:"center",width:"100%",height:"100%",minHeight:"150px"},placeholder:{display:"flex",flexDirection:"column",alignItems:"center",justifyContent:"center",gap:m.spacingVerticalM,color:m.colorNeutralForeground3,textAlign:"center",padding:m.spacingVerticalL},unknownType:{display:"flex",flexDirection:"column",alignItems:"center",justifyContent:"center",padding:m.spacingVerticalL,gap:m.spacingVerticalS,color:m.colorNeutralForeground3}}),Le=n=>{if(!n)return{};try{return JSON.parse(n)}catch{return{}}},Ne=n=>{switch(n){case e.MetricCard:return"Metric Card";case e.BarChart:return"Bar Chart";case e.LineChart:return"Line Chart";case e.AreaChart:return"Area Chart";case e.DonutChart:return"Donut Chart";case e.StatusBar:return"Status Distribution Bar";case e.Calendar:return"Calendar";case e.MiniTable:return"Mini Table";default:return`Unknown (${n})`}},B=({chartDefinition:n,chartData:i,onDrillInteraction:c,height:L=300})=>{const N=Be(),{sprk_visualtype:g,sprk_name:p,sprk_configurationjson:ye,sprk_groupbyfield:Ce}=n,t=Le(ye);if((!i||!i.dataPoints||i.dataPoints.length===0)&&g!==e.MetricCard)return a.createElement("div",{className:N.placeholder},a.createElement(f,{size:400},"No data available"),a.createElement(f,{size:200},Ne(g)," requires data to display"));const s=(i==null?void 0:i.dataPoints)||[],d=Ce||"";switch(g){case e.MetricCard:{const h=s.length>0?s[0].value:(i==null?void 0:i.totalRecords)||0,r=s.length>0?s[0].label:p;return a.createElement("div",{className:N.container},a.createElement(ke,{value:h,label:r,description:n.sprk_description,trend:t.trend,trendValue:t.trendValue,onDrillInteraction:c,drillField:d,drillValue:s.length>0?s[0].fieldValue:null,interactive:!!c,compact:t.compact}))}case e.BarChart:return a.createElement(ve,{data:s,title:t.showTitle!==!1?p:void 0,orientation:t.orientation,showLabels:t.showLabels,showLegend:t.showLegend,onDrillInteraction:c,drillField:d,height:L,responsive:!0});case e.LineChart:case e.AreaChart:return a.createElement(_e,{data:s,title:t.showTitle!==!1?p:void 0,variant:g===e.AreaChart?"area":"line",showLegend:t.showLegend,onDrillInteraction:c,drillField:d,height:L,lineColor:t.lineColor});case e.DonutChart:return a.createElement(we,{data:s,title:t.showTitle!==!1?p:void 0,innerRadius:t.innerRadius,showCenterValue:t.showCenterValue,centerLabel:t.centerLabel,showLegend:t.showLegend,onDrillInteraction:c,drillField:d,height:L});case e.StatusBar:{const h=s.map(r=>({label:r.label,value:r.value,color:r.color,fieldValue:r.fieldValue}));return a.createElement(Te,{segments:h,title:t.showTitle!==!1?p:void 0,showLabels:t.showLabels,showCounts:t.showCounts,onDrillInteraction:c,drillField:d,height:t.barHeight})}case e.Calendar:{const h=s.map(r=>({date:r.fieldValue instanceof Date?r.fieldValue:typeof r.fieldValue=="string"?new Date(r.fieldValue):new Date,count:r.value,label:r.label,fieldValue:r.fieldValue}));return a.createElement(Ve,{events:h,title:t.showTitle!==!1?p:void 0,onDrillInteraction:c,drillField:d,showNavigation:t.showNavigation!==!1})}case e.MiniTable:{const h=s.map((M,be)=>({id:`row-${be}`,values:{label:M.label,value:M.value},fieldValue:M.fieldValue})),r=[{key:"label",header:"Name",width:"60%"},{key:"value",header:"Value",width:"40%",isValue:!0}],De=t.columns;return a.createElement(Se,{items:h,columns:De||r,title:t.showTitle!==!1?p:void 0,onDrillInteraction:c,drillField:d,topN:t.topN,showRank:t.showRank!==!1})}default:return a.createElement("div",{className:N.unknownType},a.createElement(f,{size:400,weight:"semibold"},"Unsupported Visual Type"),a.createElement(f,{size:200},"Visual type ",g," is not yet supported."),a.createElement(f,{size:200},"Supported types: MetricCard, BarChart, LineChart, AreaChart, DonutChart, StatusBar, Calendar, MiniTable"))}};try{B.displayName="ChartRenderer",B.__docgenInfo={description:"ChartRenderer - Renders the appropriate chart based on visual type",displayName:"ChartRenderer",props:{chartDefinition:{defaultValue:null,description:"Chart definition from Dataverse",name:"chartDefinition",required:!0,type:{name:"IChartDefinition"}},chartData:{defaultValue:null,description:"Aggregated data for chart rendering",name:"chartData",required:!1,type:{name:"IChartData"}},onDrillInteraction:{defaultValue:null,description:"Callback when user interacts with chart for drill-through",name:"onDrillInteraction",required:!1,type:{name:"((interaction: DrillInteraction) => void)"}},height:{defaultValue:{value:"300"},description:"Height override for the chart",name:"height",required:!1,type:{name:"number"}}}}}catch{}const Me=[{label:"Active",value:45,fieldValue:"active",color:"#0078D4"},{label:"Pending",value:30,fieldValue:"pending",color:"#FFB900"},{label:"Closed",value:25,fieldValue:"closed",color:"#107C10"},{label:"Cancelled",value:10,fieldValue:"cancelled",color:"#D13438"}],j=[{label:"Jan",value:100,fieldValue:"jan"},{label:"Feb",value:120,fieldValue:"feb"},{label:"Mar",value:90,fieldValue:"mar"},{label:"Apr",value:150,fieldValue:"apr"},{label:"May",value:130,fieldValue:"may"},{label:"Jun",value:180,fieldValue:"jun"}],o={dataPoints:Me,totalRecords:110,aggregationType:O.Count,aggregationField:"statuscode",groupByField:"statuscode"},l={sprk_chartdefinitionid:"story-001",sprk_name:"Sample Chart",sprk_description:"A sample chart for testing",sprk_visualtype:e.BarChart,sprk_aggregationtype:O.Count,sprk_sourceentity:"account",sprk_groupbyfield:"statuscode",sprk_configurationjson:JSON.stringify({showTitle:!0,showLegend:!0})},dt={title:"Core/ChartRenderer",component:B,parameters:{layout:"centered",docs:{description:{component:"ChartRenderer dynamically renders the appropriate chart component based on the visual type in the chart definition. This is the central switching logic for Visual Host."}}},tags:["autodocs"],decorators:[n=>a.createElement("div",{style:{width:"600px",height:"400px",padding:"1rem"}},a.createElement(n,null))],argTypes:{chartDefinition:{description:"Chart definition from sprk_chartdefinition entity",control:"object"},chartData:{description:"Aggregated data for chart rendering",control:"object"},onDrillInteraction:{description:"Callback when user interacts with chart for drill-through",action:"drillInteraction"},height:{description:"Height override for the chart",control:{type:"number",min:100,max:600}}}},y={args:{chartDefinition:{...l,sprk_name:"Total Records",sprk_description:"Count of all records",sprk_visualtype:e.MetricCard,sprk_configurationjson:JSON.stringify({trend:"up",trendValue:12.5})},chartData:{...o,dataPoints:[{label:"Total",value:110,fieldValue:null}]},onDrillInteraction:u("drillInteraction"),height:300}},C={args:{chartDefinition:{...l,sprk_name:"Status Distribution",sprk_description:"Records by status",sprk_visualtype:e.BarChart,sprk_configurationjson:JSON.stringify({showTitle:!0,orientation:"vertical",showLegend:!1})},chartData:o,onDrillInteraction:u("drillInteraction"),height:300}},D={args:{chartDefinition:{...l,sprk_name:"Status Distribution",sprk_visualtype:e.BarChart,sprk_configurationjson:JSON.stringify({showTitle:!0,orientation:"horizontal"})},chartData:o,onDrillInteraction:u("drillInteraction"),height:300}},b={args:{chartDefinition:{...l,sprk_name:"Monthly Trend",sprk_description:"Records over time",sprk_visualtype:e.LineChart,sprk_configurationjson:JSON.stringify({showTitle:!0,showLegend:!1})},chartData:{...o,dataPoints:j,groupByField:"month"},onDrillInteraction:u("drillInteraction"),height:300}},k={args:{chartDefinition:{...l,sprk_name:"Cumulative Growth",sprk_visualtype:e.AreaChart,sprk_configurationjson:JSON.stringify({showTitle:!0,lineColor:"#0078D4"})},chartData:{...o,dataPoints:j,groupByField:"month"},onDrillInteraction:u("drillInteraction"),height:300}},v={args:{chartDefinition:{...l,sprk_name:"Status Breakdown",sprk_visualtype:e.DonutChart,sprk_configurationjson:JSON.stringify({showTitle:!0,showCenterValue:!0,centerLabel:"Total",showLegend:!0})},chartData:o,onDrillInteraction:u("drillInteraction"),height:300}},_={args:{chartDefinition:{...l,sprk_name:"Pipeline Status",sprk_visualtype:e.StatusBar,sprk_configurationjson:JSON.stringify({showTitle:!0,showLabels:!0,showCounts:!0})},chartData:o,onDrillInteraction:u("drillInteraction"),height:300}},w={args:{chartDefinition:{...l,sprk_name:"Activity Calendar",sprk_visualtype:e.Calendar,sprk_configurationjson:JSON.stringify({showTitle:!0,showNavigation:!0})},chartData:{...o,dataPoints:[{label:"Meeting",value:3,fieldValue:new Date().toISOString()},{label:"Call",value:2,fieldValue:new Date().toISOString()}]},onDrillInteraction:u("drillInteraction"),height:400}},T={args:{chartDefinition:{...l,sprk_name:"Top Items",sprk_visualtype:e.MiniTable,sprk_configurationjson:JSON.stringify({showTitle:!0,showRank:!0,topN:5})},chartData:o,onDrillInteraction:u("drillInteraction"),height:300}},V={args:{chartDefinition:{...l,sprk_name:"Empty Chart",sprk_visualtype:e.BarChart},chartData:{dataPoints:[],totalRecords:0,aggregationType:O.Count},height:300}},S={args:{chartDefinition:{...l,sprk_name:"Display Only",sprk_visualtype:e.BarChart},chartData:o,onDrillInteraction:void 0,height:300}},I={render:()=>{const n=[{type:e.MetricCard,name:"MetricCard"},{type:e.BarChart,name:"BarChart"},{type:e.LineChart,name:"LineChart"},{type:e.DonutChart,name:"DonutChart"},{type:e.StatusBar,name:"StatusBar"},{type:e.MiniTable,name:"MiniTable"}];return a.createElement("div",{style:{display:"grid",gridTemplateColumns:"repeat(2, 1fr)",gap:"1rem",padding:"1rem",width:"1000px"}},n.map(({type:i,name:c})=>a.createElement("div",{key:i,style:{border:"1px solid #e0e0e0",borderRadius:"8px",padding:"1rem",height:"300px"}},a.createElement(B,{chartDefinition:{...l,sprk_chartdefinitionid:`gallery-${i}`,sprk_name:c,sprk_visualtype:i,sprk_configurationjson:JSON.stringify({showTitle:!0,showLegend:!0})},chartData:i===e.MetricCard?{...o,dataPoints:[{label:"Total",value:110,fieldValue:null}]}:i===e.LineChart?{...o,dataPoints:j}:o,onDrillInteraction:u(`drill-${c}`),height:250}))))},decorators:[n=>a.createElement("div",{style:{width:"1100px"}},a.createElement(n,null))]};var P,E,R;y.parameters={...y.parameters,docs:{...(P=y.parameters)==null?void 0:P.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Total Records",
      sprk_description: "Count of all records",
      sprk_visualtype: VisualType.MetricCard,
      sprk_configurationjson: JSON.stringify({
        trend: "up",
        trendValue: 12.5
      })
    },
    chartData: {
      ...baseChartData,
      dataPoints: [{
        label: "Total",
        value: 110,
        fieldValue: null
      }]
    },
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(R=(E=y.parameters)==null?void 0:E.docs)==null?void 0:R.source}}};var x,J,A;C.parameters={...C.parameters,docs:{...(x=C.parameters)==null?void 0:x.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Status Distribution",
      sprk_description: "Records by status",
      sprk_visualtype: VisualType.BarChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        orientation: "vertical",
        showLegend: false
      })
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(A=(J=C.parameters)==null?void 0:J.docs)==null?void 0:A.source}}};var F,$,z;D.parameters={...D.parameters,docs:{...(F=D.parameters)==null?void 0:F.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Status Distribution",
      sprk_visualtype: VisualType.BarChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        orientation: "horizontal"
      })
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(z=($=D.parameters)==null?void 0:$.docs)==null?void 0:z.source}}};var H,q,G;b.parameters={...b.parameters,docs:{...(H=b.parameters)==null?void 0:H.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Monthly Trend",
      sprk_description: "Records over time",
      sprk_visualtype: VisualType.LineChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showLegend: false
      })
    },
    chartData: {
      ...baseChartData,
      dataPoints: monthlyDataPoints,
      groupByField: "month"
    },
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(G=(q=b.parameters)==null?void 0:q.docs)==null?void 0:G.source}}};var U,K,Q;k.parameters={...k.parameters,docs:{...(U=k.parameters)==null?void 0:U.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Cumulative Growth",
      sprk_visualtype: VisualType.AreaChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        lineColor: "#0078D4"
      })
    },
    chartData: {
      ...baseChartData,
      dataPoints: monthlyDataPoints,
      groupByField: "month"
    },
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(Q=(K=k.parameters)==null?void 0:K.docs)==null?void 0:Q.source}}};var W,X,Y;v.parameters={...v.parameters,docs:{...(W=v.parameters)==null?void 0:W.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Status Breakdown",
      sprk_visualtype: VisualType.DonutChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showCenterValue: true,
        centerLabel: "Total",
        showLegend: true
      })
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(Y=(X=v.parameters)==null?void 0:X.docs)==null?void 0:Y.source}}};var Z,ee,te;_.parameters={..._.parameters,docs:{...(Z=_.parameters)==null?void 0:Z.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Pipeline Status",
      sprk_visualtype: VisualType.StatusBar,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showLabels: true,
        showCounts: true
      })
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(te=(ee=_.parameters)==null?void 0:ee.docs)==null?void 0:te.source}}};var ae,re,ne;w.parameters={...w.parameters,docs:{...(ae=w.parameters)==null?void 0:ae.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Activity Calendar",
      sprk_visualtype: VisualType.Calendar,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showNavigation: true
      })
    },
    chartData: {
      ...baseChartData,
      dataPoints: [{
        label: "Meeting",
        value: 3,
        fieldValue: new Date().toISOString()
      }, {
        label: "Call",
        value: 2,
        fieldValue: new Date().toISOString()
      }]
    },
    onDrillInteraction: action("drillInteraction"),
    height: 400
  }
}`,...(ne=(re=w.parameters)==null?void 0:re.docs)==null?void 0:ne.source}}};var ie,oe,se;T.parameters={...T.parameters,docs:{...(ie=T.parameters)==null?void 0:ie.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Top Items",
      sprk_visualtype: VisualType.MiniTable,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showRank: true,
        topN: 5
      })
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300
  }
}`,...(se=(oe=T.parameters)==null?void 0:oe.docs)==null?void 0:se.source}}};var le,ce,ue;V.parameters={...V.parameters,docs:{...(le=V.parameters)==null?void 0:le.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Empty Chart",
      sprk_visualtype: VisualType.BarChart
    },
    chartData: {
      dataPoints: [],
      totalRecords: 0,
      aggregationType: AggregationType.Count
    },
    height: 300
  }
}`,...(ue=(ce=V.parameters)==null?void 0:ce.docs)==null?void 0:ue.source}}};var pe,de,he;S.parameters={...S.parameters,docs:{...(pe=S.parameters)==null?void 0:pe.docs,source:{originalSource:`{
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Display Only",
      sprk_visualtype: VisualType.BarChart
    },
    chartData: baseChartData,
    onDrillInteraction: undefined,
    height: 300
  }
}`,...(he=(de=S.parameters)==null?void 0:de.docs)==null?void 0:he.source}}};var me,ge,fe;I.parameters={...I.parameters,docs:{...(me=I.parameters)==null?void 0:me.docs,source:{originalSource:`{
  render: () => {
    const visualTypes = [{
      type: VisualType.MetricCard,
      name: "MetricCard"
    }, {
      type: VisualType.BarChart,
      name: "BarChart"
    }, {
      type: VisualType.LineChart,
      name: "LineChart"
    }, {
      type: VisualType.DonutChart,
      name: "DonutChart"
    }, {
      type: VisualType.StatusBar,
      name: "StatusBar"
    }, {
      type: VisualType.MiniTable,
      name: "MiniTable"
    }];
    return <div style={{
      display: "grid",
      gridTemplateColumns: "repeat(2, 1fr)",
      gap: "1rem",
      padding: "1rem",
      width: "1000px"
    }}>\r
        {visualTypes.map(({
        type,
        name
      }) => <div key={type} style={{
        border: "1px solid #e0e0e0",
        borderRadius: "8px",
        padding: "1rem",
        height: "300px"
      }}>\r
            <ChartRenderer chartDefinition={{
          ...baseChartDefinition,
          sprk_chartdefinitionid: \`gallery-\${type}\`,
          sprk_name: name,
          sprk_visualtype: type,
          sprk_configurationjson: JSON.stringify({
            showTitle: true,
            showLegend: true
          })
        }} chartData={type === VisualType.MetricCard ? {
          ...baseChartData,
          dataPoints: [{
            label: "Total",
            value: 110,
            fieldValue: null
          }]
        } : type === VisualType.LineChart ? {
          ...baseChartData,
          dataPoints: monthlyDataPoints
        } : baseChartData} onDrillInteraction={action(\`drill-\${name}\`)} height={250} />\r
          </div>)}\r
      </div>;
  },
  decorators: [Story => <div style={{
    width: "1100px"
  }}>\r
        <Story />\r
      </div>]
}`,...(fe=(ge=I.parameters)==null?void 0:ge.docs)==null?void 0:fe.source}}};const ht=["MetricCard","BarChartVertical","BarChartHorizontal","LineChart","AreaChart","DonutChart","StatusDistributionBar","Calendar","MiniTable","NoData","NoDrillThrough","AllVisualTypes"];export{I as AllVisualTypes,k as AreaChart,D as BarChartHorizontal,C as BarChartVertical,w as Calendar,v as DonutChart,b as LineChart,y as MetricCard,T as MiniTable,V as NoData,S as NoDrillThrough,_ as StatusDistributionBar,ht as __namedExportsOrder,dt as default};
