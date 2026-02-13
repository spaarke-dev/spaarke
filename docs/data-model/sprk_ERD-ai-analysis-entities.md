Table sprk_analysis {
	sprk_analysisid uniqueidentifier [pk]
	sprk_actionid lookup 
	sprk_playbook lookup 
}
Ref: sprk_analysis.sprk_actionid > sprk_analysisaction.sprk_analysisactionid
Ref: sprk_analysis.sprk_playbook > sprk_analysisplaybook.sprk_analysisplaybookid

Table sprk_analysis_knowledge {
	sprk_analysis_knowledgeid uniqueidentifier [pk]
}

Table sprk_analysis_skill {
	sprk_analysis_skillid uniqueidentifier [pk]
}

Table sprk_analysis_tool {
	sprk_analysis_toolid uniqueidentifier [pk]
}

Table sprk_analysisaction {
	sprk_analysisactionid uniqueidentifier [pk]
	sprk_actiontype lookup 
	sprk_actiontypeid lookup 
	sprk_analysisid lookup 
}
Ref: sprk_analysisaction.sprk_actiontype > sprk_analysisactiontype.sprk_analysisactiontypeid
Ref: sprk_analysisaction.sprk_actiontypeid > sprk_analysisactiontype.sprk_analysisactiontypeid
Ref: sprk_analysisaction.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_analysisactiontype {
	sprk_analysisactiontypeid uniqueidentifier [pk]
}

Table sprk_analysischatmessage {
	sprk_analysischatmessageid uniqueidentifier [pk]
	sprk_analysisid lookup 
}
Ref: sprk_analysischatmessage.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_analysisemailmetadata {
	sprk_analysisemailmetadataid uniqueidentifier [pk]
	sprk_analysisid lookup 
}
Ref: sprk_analysisemailmetadata.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_analysisknowledge {
	sprk_analysisknowledgeid uniqueidentifier [pk]
	sprk_analysisid lookup 
}
Ref: sprk_analysisknowledge.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_analysisoutput {
	sprk_analysisoutputid uniqueidentifier [pk]
	sprk_analysisid lookup 
}
Ref: sprk_analysisoutput.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_analysisplaybook {
	sprk_analysisplaybookid uniqueidentifier [pk]
}

Table sprk_analysisplaybook_action {
	sprk_analysisplaybook_actionid uniqueidentifier [pk]
}

Table sprk_analysisplaybook_analysisoutput {
	sprk_analysisplaybook_analysisoutputid uniqueidentifier [pk]
}

Table sprk_analysisplaybook_mattertype {
	sprk_analysisplaybook_mattertypeid uniqueidentifier [pk]
}

Table sprk_analysisskill {
	sprk_analysisskillid uniqueidentifier [pk]
	sprk_analysisid lookup 
}
Ref: sprk_analysisskill.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_analysistool {
	sprk_analysistoolid uniqueidentifier [pk]
	sprk_analysisid lookup 
}
Ref: sprk_analysistool.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_analysisworkingversion {
	sprk_analysisworkingversionid uniqueidentifier [pk]
	sprk_analysisid lookup 
}
Ref: sprk_analysisworkingversion.sprk_analysisid > sprk_analysis.sprk_analysisid

Table sprk_playbook_knowledge {
	sprk_playbook_knowledgeid uniqueidentifier [pk]
}

Table sprk_playbook_skill {
	sprk_playbook_skillid uniqueidentifier [pk]
}

Table sprk_playbook_tool {
	sprk_playbook_toolid uniqueidentifier [pk]
}

