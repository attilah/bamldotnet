// Client configuration for testing
// This is a minimal configuration for code generation testing

// Default client for testing
client<llm> TestClient {
  provider openai
  options {
    model "gpt-4"
    api_key env.OPENAI_API_KEY
  }
}