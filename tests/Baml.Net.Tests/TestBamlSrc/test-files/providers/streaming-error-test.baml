// Test function that always fails for streaming error propagation testing
function StreamAlwaysFails(input: string) -> string {
  client "openai/gpt-0-noexist"  // Non-existent model to trigger error
  prompt #"
    {{ _.role('system') }}
    You are a helpful assistant.

    {{ _.role('user') }}
    Write a story about {{ input }}
  "#
}