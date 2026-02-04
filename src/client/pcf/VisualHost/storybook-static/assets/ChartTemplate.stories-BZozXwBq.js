import{r as i}from"./index-D4H_InIO.js";import{a as _}from"./index-B-lxVbXh.js";import{A as T,V as a}from"./index-DdRfOkZ2.js";import{_ as S,m as U,T as d,t as n}from"./Text-C0fEdKO1.js";import{u as K,C as Q}from"./Card-8YjWmAfr.js";import{m as X,o as m,b as Y,g as Z,j as ee,c as g,d as re}from"./jsx-runtime-D93ddY-N.js";import{u as ie}from"./useId-o6sUQ3C5.js";import"./v4-CtRu48qb.js";import"./constants-C-FBwxq0.js";import"./useIsomorphicLayoutEffect-DxaFQ3i0.js";import"./keys-BStMjYrg.js";import"./useFocusWithin-DRp-habq.js";import"./__resetStyles.esm-CYXmtSuc.js";import"./jsx-runtime-Dz47noOj.js";const G={root:"fui-CardHeader",image:"fui-CardHeader__image",header:"fui-CardHeader__header",description:"fui-CardHeader__description",action:"fui-CardHeader__action"},te=S({root:{Bkc6ea2:"fkufhic",Bt984gj:"f122n59"},image:{mc9l5x:"ftuwxu6",t21cq0:["fql5097","f6yss9k"]},header:{mc9l5x:"f22iagw"},description:{mc9l5x:"f22iagw"},action:{Frg6f3:["f6yss9k","fql5097"],rjrqlh:"fs9eatd",Boue1pl:["fxoo9ru","f1g0ekvh"],Bhz1vi0:"f1m6zfxz",etxrgc:["f1g0ekvh","fxoo9ru"],Bdua9ef:"f1sret3r",cbfxhg:"fs4gbcv"}},{d:[".fkufhic{--fui-CardHeader--gap:12px;}",".f122n59{align-items:center;}",".ftuwxu6{display:inline-flex;}",".fql5097{margin-right:var(--fui-CardHeader--gap);}",".f6yss9k{margin-left:var(--fui-CardHeader--gap);}",".f22iagw{display:flex;}"],m:[["@media (forced-colors: active){.fs9eatd .fui-Button,.fs9eatd .fui-Link{border-top-color:currentColor;}}",{m:"(forced-colors: active)"}],["@media (forced-colors: active){.f1g0ekvh .fui-Button,.f1g0ekvh .fui-Link{border-left-color:currentColor;}.fxoo9ru .fui-Button,.fxoo9ru .fui-Link{border-right-color:currentColor;}}",{m:"(forced-colors: active)"}],["@media (forced-colors: active){.f1m6zfxz .fui-Button,.f1m6zfxz .fui-Link{border-bottom-color:currentColor;}}",{m:"(forced-colors: active)"}],["@media (forced-colors: active){.f1sret3r .fui-Button,.f1sret3r .fui-Link{color:currentColor;}}",{m:"(forced-colors: active)"}],["@media (forced-colors: active){.fs4gbcv .fui-Button,.fs4gbcv .fui-Link{outline-color:currentColor;}}",{m:"(forced-colors: active)"}]]}),ae=S({root:{mc9l5x:"f13qh94s",t4k1zu:"f8a668j"},image:{Br312pm:"fwpfdsa",Ijaq50:"fldnz9j"},header:{Br312pm:"fd46tj4",Ijaq50:"f16hsg94"},description:{Br312pm:"fd46tj4",Ijaq50:"faunodf"},action:{Br312pm:"fis13di",Ijaq50:"fldnz9j"}},{d:[".f13qh94s{display:grid;}",".f8a668j{grid-auto-columns:min-content 1fr min-content;}",".fwpfdsa{grid-column-start:1;}",".fldnz9j{grid-row-start:span 2;}",".fd46tj4{grid-column-start:2;}",".f16hsg94{grid-row-start:1;}",".faunodf{grid-row-start:2;}",".fis13di{grid-column-start:3;}"]}),ne=S({root:{mc9l5x:"f22iagw"},header:{Bh6795r:"fqerorx"},image:{},description:{},action:{}},{d:[".f22iagw{display:flex;}",".fqerorx{flex-grow:1;}"]}),oe=e=>{"use no memo";const r=te(),t=ae(),c=ne(),p=e.description?t:c,o=s=>{var l;return X(G[s],r[s],p[s],(l=e[s])===null||l===void 0?void 0:l.className)};return e.root.className=o("root"),e.image&&(e.image.className=o("image")),e.header&&(e.header.className=o("header")),e.description&&(e.description.className=o("description")),e.action&&(e.action.className=o("action")),e};function se(e){function r(t){return i.isValidElement(t)&&!!t.props.id}return i.Children.toArray(e).find(r)}function ce(e,r,t){return e||(r!=null&&r.props.id?r.props.id:t)}const le=(e,r)=>{const{image:t,header:c,description:p,action:o}=e,{selectableA11yProps:{referenceId:s,setReferenceId:l}}=K(),B=i.useRef(null),b=i.useRef(!1),I=ie(G.header,s),u=m(c,{renderByDefault:!0,defaultProps:{ref:B,id:b.current?void 0:s},elementType:"div"});return i.useEffect(()=>{var x;const J=b.current||(x=B.current)===null||x===void 0?void 0:x.id,D=se(u==null?void 0:u.children);b.current=!!D,l(ce(J,D,I))},[I,c,u,l]),{components:{root:"div",image:"div",header:"div",description:"div",action:"div"},root:Y(Z("div",{ref:r,...e}),{elementType:"div"}),image:m(t,{elementType:"div"}),header:u,description:m(p,{elementType:"div"}),action:m(o,{elementType:"div"})}},de=e=>ee(e.root,{children:[e.image&&g(e.image,{}),e.header&&g(e.header,{}),e.description&&g(e.description,{}),e.action&&g(e.action,{})]}),O=i.forwardRef((e,r)=>{const t=le(e,r);return oe(t),re("useCardHeaderStyles_unstable")(t),de(t)});O.displayName="CardHeader";const f={sprk_chartdefinitionid:"sample-001",sprk_name:"Sample Chart",sprk_description:"A sample chart for Storybook development",sprk_visualtype:a.MetricCard,sprk_aggregationtype:T.Count,sprk_sourceentity:"account",sprk_configurationjson:JSON.stringify({primaryColor:"#0078D4",showTrend:!0})},pe=U({placeholder:{display:"flex",flexDirection:"column",alignItems:"center",justifyContent:"center",minHeight:"200px",padding:n.spacingVerticalL,backgroundColor:n.colorNeutralBackground2,borderRadius:n.borderRadiusMedium},visualType:{color:n.colorBrandForeground1,fontWeight:n.fontWeightSemibold},drillInfo:{marginTop:n.spacingVerticalM,padding:n.spacingVerticalS,backgroundColor:n.colorNeutralBackground3,borderRadius:n.borderRadiusSmall,fontFamily:"monospace",fontSize:n.fontSizeBase200}}),W=({definition:e,onDrillInteraction:r})=>{const t=pe(),c=()=>{r&&r({field:"sample_field",operator:"eq",value:"sample_value",label:"Sample Filter"})},p=a[e.sprk_visualtype],o=T[e.sprk_aggregationtype||0];return i.createElement(Q,{onClick:c,style:{cursor:r?"pointer":"default"}},i.createElement(O,{header:i.createElement(d,{weight:"semibold"},e.sprk_name),description:i.createElement(d,{size:200},e.sprk_description)}),i.createElement("div",{className:t.placeholder},i.createElement(d,{className:t.visualType},p),i.createElement(d,{size:200},"Coming in Task 010-016"),i.createElement(d,{size:100},"Aggregation: ",o),i.createElement(d,{size:100},"Source: ",e.sprk_sourceentity),r&&i.createElement("div",{className:t.drillInfo},"Click to trigger drill interaction")))},Be={title:"Charts/Template",component:W,parameters:{layout:"centered",docs:{description:{component:"Template for chart component stories. Use this as a starting point when implementing actual chart components."}}},tags:["autodocs"],argTypes:{definition:{description:"Chart definition from sprk_chartdefinition entity",control:"object"},onDrillInteraction:{description:"Callback when user clicks to drill through",action:"drillInteraction"}}},y={args:{definition:f,onDrillInteraction:_("drillInteraction")}},h={args:{definition:{...f,sprk_chartdefinitionid:"metric-001",sprk_name:"Total Accounts",sprk_description:"Count of active accounts",sprk_visualtype:a.MetricCard,sprk_aggregationtype:T.Count},onDrillInteraction:_("drillInteraction")}},C={args:{definition:{...f,sprk_chartdefinitionid:"bar-001",sprk_name:"Revenue by Region",sprk_description:"Sum of revenue grouped by region",sprk_visualtype:a.BarChart,sprk_aggregationtype:T.Sum},onDrillInteraction:_("drillInteraction")}},k={args:{definition:{...f,sprk_name:"Static Display",sprk_description:"Chart without drill-through capability"},onDrillInteraction:void 0}},v={render:()=>{const e=[a.MetricCard,a.BarChart,a.LineChart,a.DonutChart,a.StatusBar,a.Calendar,a.MiniTable];return i.createElement("div",{style:{display:"grid",gridTemplateColumns:"repeat(2, 1fr)",gap:"1rem",padding:"1rem"}},e.map(r=>i.createElement(W,{key:r,definition:{...f,sprk_chartdefinitionid:`type-${r}`,sprk_name:a[r],sprk_visualtype:r},onDrillInteraction:_(`drill-${a[r]}`)})))}};var w,j,V;y.parameters={...y.parameters,docs:{...(w=y.parameters)==null?void 0:w.docs,source:{originalSource:`{
  args: {
    definition: sampleChartDefinition,
    onDrillInteraction: action("drillInteraction")
  }
}`,...(V=(j=y.parameters)==null?void 0:j.docs)==null?void 0:V.source}}};var E,z,H;h.parameters={...h.parameters,docs:{...(E=h.parameters)==null?void 0:E.docs,source:{originalSource:`{
  args: {
    definition: {
      ...sampleChartDefinition,
      sprk_chartdefinitionid: "metric-001",
      sprk_name: "Total Accounts",
      sprk_description: "Count of active accounts",
      sprk_visualtype: VisualType.MetricCard,
      sprk_aggregationtype: AggregationType.Count
    },
    onDrillInteraction: action("drillInteraction")
  }
}`,...(H=(z=h.parameters)==null?void 0:z.docs)==null?void 0:H.source}}};var N,R,q;C.parameters={...C.parameters,docs:{...(N=C.parameters)==null?void 0:N.docs,source:{originalSource:`{
  args: {
    definition: {
      ...sampleChartDefinition,
      sprk_chartdefinitionid: "bar-001",
      sprk_name: "Revenue by Region",
      sprk_description: "Sum of revenue grouped by region",
      sprk_visualtype: VisualType.BarChart,
      sprk_aggregationtype: AggregationType.Sum
    },
    onDrillInteraction: action("drillInteraction")
  }
}`,...(q=(R=C.parameters)==null?void 0:R.docs)==null?void 0:q.source}}};var A,M,L;k.parameters={...k.parameters,docs:{...(A=k.parameters)==null?void 0:A.docs,source:{originalSource:`{
  args: {
    definition: {
      ...sampleChartDefinition,
      sprk_name: "Static Display",
      sprk_description: "Chart without drill-through capability"
    },
    onDrillInteraction: undefined
  }
}`,...(L=(M=k.parameters)==null?void 0:M.docs)==null?void 0:L.source}}};var F,P,$;v.parameters={...v.parameters,docs:{...(F=v.parameters)==null?void 0:F.docs,source:{originalSource:`{
  render: () => {
    const visualTypes = [VisualType.MetricCard, VisualType.BarChart, VisualType.LineChart, VisualType.DonutChart, VisualType.StatusBar, VisualType.Calendar, VisualType.MiniTable];
    return <div style={{
      display: "grid",
      gridTemplateColumns: "repeat(2, 1fr)",
      gap: "1rem",
      padding: "1rem"
    }}>\r
        {visualTypes.map(visualType => <ChartPlaceholder key={visualType} definition={{
        ...sampleChartDefinition,
        sprk_chartdefinitionid: \`type-\${visualType}\`,
        sprk_name: VisualType[visualType],
        sprk_visualtype: visualType
      }} onDrillInteraction={action(\`drill-\${VisualType[visualType]}\`)} />)}\r
      </div>;
  }
}`,...($=(P=v.parameters)==null?void 0:P.docs)==null?void 0:$.source}}};const Ie=["Default","MetricCard","BarChart","NoDrill","AllVisualTypes"];export{v as AllVisualTypes,C as BarChart,y as Default,h as MetricCard,k as NoDrill,Ie as __namedExportsOrder,Be as default};
