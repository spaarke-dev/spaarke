"""
Wrapper module for AI Foundry Agent with MCP Integration.

This package exposes the necessary methods to interact with Azure AI Foundry agents
through the Model Context Protocol (MCP).
"""

import os, time
import yaml
import logging
from dotenv import load_dotenv
from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential
from azure.ai.agents.models import (
    ListSortOrder,
    McpTool,
    RequiredMcpToolCall,
    RunStepActivityDetails,
    SubmitToolApprovalAction,
    ToolApproval,
)

# Global variables to store configuration
config = None
agent_name = None
mcp_server_url = None
mcp_server_label = None
allowed_tools = None
agent_instructions = None
approval_mode = None
logging_enabled = None
log_path = None
delete_agent_after_run = None
ignore_existing_agent = None
model_deployment_name = None
project_endpoint = None
auth_token = None
logging_initialized = False

def _load_config(input_agent_name):
    """Load configuration from YAML file and environment variables"""
    global config, agent_name, agent_description, mcp_server_url, mcp_server_label, allowed_tools
    global agent_instructions, approval_mode, logging_enabled, log_path
    global delete_agent_after_run, ignore_existing_agent, model_deployment_name, project_endpoint
    global auth_token, logging_initialized
    
    # Get the directory where this script is located
    script_dir = os.path.dirname(os.path.abspath(__file__))

    # Load environment variables from ai_foundry.env file in the script directory
    load_dotenv(os.path.join(script_dir, 'ai_foundry.env'))

    # Get AI Foundry Configuration from environment variables
    model_deployment_name = os.getenv("MODEL_DEPLOYMENT_NAME")
    project_endpoint = os.getenv("PROJECT_ENDPOINT")

    # Load agent configuration from YAML file
    try:
        # Look for agent_config.yaml in the same directory as this script
        config_file_path = os.path.join(script_dir, 'agent_config.yaml')
        with open(config_file_path, 'r') as config_file:
            agent_config = yaml.safe_load(config_file)
    except FileNotFoundError:
        print(f"Error: agent_config.yaml file not found at {config_file_path}")
        print("Please ensure agent_config.yaml exists in the same directory as agent.py")
        exit(1)
    except yaml.YAMLError as e:
        print(f"Error parsing YAML file: {e}")
        exit(1)

    # Get the specified agent configuration
    agent_name = input_agent_name
    if agent_name not in agent_config:
        available_agents = list(agent_config.keys())
        print(f"Error: Agent '{agent_name}' not found in configuration.")
        print(f"Available agents: {available_agents}")
        exit(1)
    config = agent_config[agent_name]

    # Get Agent Configuration from agent_config.yaml
    mcp_server_url = config.get("MCP_Server_URL")
    mcp_server_label = config.get("MCP_Server_Label")
    allowed_tools = config.get("Allowed_Tools", [])
    agent_description = config.get("Agent_Description", "")
    agent_instructions = config.get("Agent_Instruction")
    approval_mode = config.get("Approval_Mode", "never")
    logging_enabled = config.get("Logging", True)
    log_path = config.get("Log_Path", "logs/agent_logs.txt")
    delete_agent_after_run = config.get("Delete_Agent_After_Run", False)
    ignore_existing_agent = config.get("Ignore_Existing_Agent", False)
    auth_token = config.get("Auth_Token", "")
    logging_initialized = False  # Reset logging flag for new config

def _log_message(message):
    """Setup logging function"""
    global logging_initialized
    if logging_enabled:
        os.makedirs(os.path.dirname(log_path), exist_ok=True)
        with open(log_path, 'a', encoding='utf-8') as log_file:
            # Only write the "Starting Logging" message once
            if not logging_initialized:
                log_file.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')} - Starting Logging for Agent {agent_name}\n")
                logging_initialized = True
            log_file.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')} - {message}\n")
    else:
        print(message)

def _project_init():
    """Initialize AI Project Client and MCP Tool"""
    # Initialize AI Project Client
    project_client = AIProjectClient(
        endpoint=project_endpoint,
        credential=DefaultAzureCredential(),
    )

    # Initialize agent MCP tool
    mcp_tool = McpTool(
        server_label=mcp_server_label,
        server_url=mcp_server_url,
        allowed_tools=allowed_tools, # Empty list means all tools are allowed
    )

    mcp_tool.set_approval_mode(approval_mode) # Set approval mode: "always", "never", "on_request"

    if auth_token:
        mcp_tool.update_headers("Authorization", f"Bearer {auth_token}") # Adding the Authentication Header for Snowflake PAT
    
    _log_message(f"Initialized MCP Tool {mcp_tool}")

    return project_client, mcp_tool

def _agent_init(agents_client, mcp_tool):
    """Check for existing agent and create agent if needed"""
    # Check if agent with the same name already exists (unless ignoring existing agents)
    existing_agent = None
    
    # Only check for existing agents if not ignoring existing agents
    if not ignore_existing_agent:
        try:
            existing_agents = agents_client.list_agents()
            
            for agent_item in existing_agents:
                if agent_item.name == agent_name: # Check by name and not ID.
                    existing_agent = agent_item
                    break
        except Exception as e:
            _log_message(f"Error listing agents: {e}")
            existing_agent = None
    else:
        _log_message("Ignoring existing agents - will create new agent")
    
    # Use existing agent if found and not ignoring existing agents
    if existing_agent and not ignore_existing_agent:
        agent = existing_agent
        _log_message(f"Using existing agent, Name: {agent_name} ID: {agent.id}")
    else:
        # Create a new agent.
        agent = agents_client.create_agent(
            model=model_deployment_name,
            name=agent_name,
            description=agent_description,
            instructions=agent_instructions,
            tools=mcp_tool.definitions,
        )
        _log_message(f"Created new agent, Name: {agent_name} ID: {agent.id}")
    _log_message(f"MCP Server: {mcp_tool.server_label} at {mcp_tool.server_url}")
    
    return agent

def _agent_run(agents_client, agent, mcp_tool, user_message, agent_name, thread_id=None):
    """Create threads, pass messages, handle approvals, and return conversation results"""

    # Create or get thread for communication
    if thread_id:    
        try:
            # Use existing thread if provided
            thread = agents_client.threads.get(thread_id=thread_id)
            _log_message(f"Using existing thread, ID: {thread.id}. Details: {thread}")
        except Exception as e:
            _log_message(f"Error fetching thread {thread_id}: {e}")
    else:
        # Create thread for communication
        try:
            thread = agents_client.threads.create()
            _log_message(f"Created thread, ID: {thread.id}")
        except Exception as e:
            _log_message(f"Error creating thread: {e}")

    # Create message to thread
    try:
        message = agents_client.messages.create(
            thread_id=thread.id,
            role="user",
            content=user_message,
        )
        _log_message(f"Created message, ID: {message.id}")
    except Exception as e:
        _log_message(f"Error creating message: {e}")

    # Create and process agent run in thread with MCP tools
    try:
        _log_message(f"Starting run for agent ID: {agent.id} in thread ID: {thread.id}")
        run = agents_client.runs.create(
            thread_id=thread.id,
            agent_id=agent.id,
            tool_resources=mcp_tool.resources
        )
        _log_message(f"Created run, ID: {run.id}")
    except Exception as e:
        _log_message(f"Error creating run: {e}")

    # Poll for run status and handle tool approvals if needed
    while run.status in ["queued", "in_progress", "requires_action"]:
        time.sleep(1)
        run = agents_client.runs.get(
            thread_id=thread.id,
            run_id=run.id
        )

        # Handle Tools Approvals and Terminate if no tool calls   
        if run.status == "requires_action" and isinstance(run.required_action, SubmitToolApprovalAction):
            tool_calls = run.required_action.submit_tool_approval.tool_calls
            if not tool_calls:
                _log_message("No tool calls provided - cancelling run")
                agents_client.runs.cancel(
                    thread_id=thread.id,
                    run_id=run.id
                )
                break
            _log_message(f"Run requires action - {len(tool_calls)} tool calls to approve")
            # Auto-approve all tool calls for this example, implement your own approval logic if needed
            tool_approvals = []
            for tool_call in tool_calls:
                if isinstance(tool_call, RequiredMcpToolCall):
                    try:
                        _log_message(f"Approving tool call: {tool_call}")
                        tool_approvals.append(
                            ToolApproval(
                                tool_call_id=tool_call.id,
                                approve=True,
                                headers=mcp_tool.headers,
                        )
                        )
                    except Exception as e:
                        _log_message(f"Error approving tool_call {tool_call.id}: {e}")

            _log_message(f"tool_approvals: {tool_approvals}")
            if tool_approvals:
                agents_client.runs.submit_tool_outputs(
                    thread_id=thread.id,
                    run_id=run.id,
                    tool_approvals=tool_approvals
                )

        _log_message(f"Current run status: {run.status}")

    _log_message(f"Run completed with status: {run.status}")
    if run.status == "failed":
        _log_message(f"Run failed: {run.last_error}")

    # Display run steps and tool calls
    run_steps = agents_client.run_steps.list(
        thread_id=thread.id,
        run_id=run.id
    )

    # Loop through each step
    for step in run_steps:
        _log_message(f"Step {step['id']} status: {step['status']}")

        # Check if there are tool calls in the step details
        step_details = step.get("step_details", {})
        tool_calls = step_details.get("tool_calls", [])

        if tool_calls:
            _log_message("  MCP Tool calls:")
            for call in tool_calls:
                _log_message(f"    Tool Call ID: {call.get('id')}")
                _log_message(f"    Name: {call.get('name')}")
                _log_message(f"    Type: {call.get('type')}")
        if isinstance(step_details, RunStepActivityDetails):
            for activity in step_details.activities:
                for function_name, function_definition in activity.tools.items():
                    _log_message(
                        f'  The function {function_name} with description "{function_definition.description}" will be called.:'
                    )
                    if len(function_definition.parameters) > 0:
                        _log_message("  Function parameters:")
                        for argument, func_argument in function_definition.parameters.properties.items():
                            _log_message(f"      {argument}")
                            _log_message(f"      Type: {func_argument.type}")
                            _log_message(f"      Description: {func_argument.description}")
                    else:
                        _log_message("This function has no parameters")

        _log_message("")  # add an extra newline between steps

    # Fetch and return all messages
    messages = agents_client.messages.list(
        thread_id=thread.id, 
        order=ListSortOrder.ASCENDING
        )
    _log_message("Conversation:")
    _log_message("-" * 50)
    
    conversation_results = []
    for msg in messages:
        if msg.text_messages:
            last_text = msg.text_messages[-1]
            role = msg.role.upper()
            content = last_text.text.value
            conversation_results.append({"role": role, "content": content})
    
    _log_message(f"response: {conversation_results}")
    
    # Return in the specified JSON format with metadata
    return {
        "agent_name": agent_name,
        "agent_id": agent.id,
        "thread_id": thread.id,
        "message_id": message.id,
        "response": conversation_results
    }

def _agent_delete(agents_client, agent, thread_id):
    """Delete the agent"""
    try:
        agents_client.threads.delete(thread_id=thread_id) # Delete the thread first
        _log_message(f"Deleted thread ID: {thread_id}")   
        agents_client.delete_agent(agent_id=agent.id)
        _log_message(f"Deleted agent ID: {agent.id}")
        return True
    except Exception as e:
        _log_message(f"Error deleting thread {thread_id} agent {agent.id}: {e}")
        return False

def _run_agent_with_message(agent_name, user_message, thread_id=None):
    """Main function to run the complete agent workflow with a custom message"""
    # Load configuration
    _load_config(agent_name)
    
    # Initialize project and MCP tool
    project_client, mcp_tool = _project_init()
    
    # Create agent with MCP tool and process agent run
    with project_client:
        agents_client = project_client.agents
        
        # Initialize or get existing agent
        agent = _agent_init(agents_client, mcp_tool)
        
        # Run the agent with the user message
        conversation_results = _agent_run(agents_client, agent, mcp_tool, user_message, agent_name, thread_id)
        
        # Delete the agent after run if set to True
        if delete_agent_after_run:
            _agent_delete(agents_client, agent, conversation_results.get("thread_id"))
        
        return conversation_results

def invoke_agent(agent_name, user_message, thread_id=None) -> dict:
    """
    Public method to invoke the agent with the specified agent name and user message.
    
    Args:
        agent_name (str): The name of the agent configuration to use
        user_message (str): The message to send to the agent
        
    Returns:
        JSON Response
    """
    results = _run_agent_with_message(agent_name, user_message, thread_id)
    return results

def _main():
    """Private main function for standalone script execution"""
    # values for standalone execution
    agent_name = "snowflake-cortex-mcp"
    user_message = "Tell me about the call with Securebank?"
    
    results = invoke_agent(agent_name, user_message)
    print(results)

if __name__ == "__main__":
    _main()