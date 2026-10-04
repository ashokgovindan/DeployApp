using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace DeployApp
{

    class ServiceNowChangeAutomation
    {
        static void Main()
        {
            // 1. Initialize WebDriver and explicit wait timers
            IWebDriver driver = new ChromeDriver();
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(12));

            try
            {
                // Note: Make sure your driver session handles any required login credentials 
                // before executing the direct landing page step below.

                // ==========================================
                // PART 1: LANDING PAGE & MODEL SELECTION
                // ==========================================
                Console.WriteLine("Navigating to Change Management creation page...");
                driver.Navigate().GoToUrl("https://dev203974.service-now.com/now/change-management/create-change");

                // Select the "Normal" change model card
                Console.WriteLine("Selecting 'Normal' Change template...");
                System.Threading.Thread.Sleep(15000);

                IWebElement normalCard = wait.Until(d => d.FindElement(By.XPath("//div[contains(@class, 'card') or contains(@class, 'model')]//h2[text()='Normal'] | //*[text()='Normal']/ancestor::button")));
                normalCard.Click();

                // Click the "Continue" button at the top right
                IWebElement continueButton = wait.Until(d => d.FindElement(By.XPath("//button[contains(., 'Continue')]")));
                if (continueButton.Enabled)
                {
                    continueButton.Click();
                    Console.WriteLine("Clicked 'Continue'. Waiting for form generation...");
                }
                else
                {
                    throw new Exception("The 'Continue' button remained disabled after selecting the card.");
                }

                // ==========================================
                // PART 2: FORM PAGE VALIDATION & DATA FILL
                // ==========================================
                // Switch into the ServiceNow main content frame if nested inside the frame hierarchy
                try
                {
                    wait.Until(d => d.FindElement(By.Id("gsft_main")));
                    driver.SwitchTo().Frame("gsft_main");
                    Console.WriteLine("Switched into the forms workspace context.");
                }
                catch (WebDriverTimeoutException)
                {
                    // Proceed if the landing page redirected directly without nesting frames
                }

                // Validate that the new Change Request page is visible
                IWebElement numberField = wait.Until(d => d.FindElement(By.Id("change_request.number")));
                if (numberField.Displayed)
                {
                    // Change line 64 to this:
                    string? changeNumber = numberField.GetAttribute("value");
                    Console.WriteLine($"Validation Success: Change Request form is visible ({changeNumber ?? "Unknown"}).");
                }

                // Fill in the primary Header Reference fields
                // Note: We use 'sys_display.' prefix for auto-complete lookup wrappers
                Console.WriteLine("Filling core reference metadata fields...");
                driver.FindElement(By.Id("sys_display.change_request.business_service")).SendKeys("My Service Name" + Keys.Enter);
                driver.FindElement(By.Id("sys_display.change_request.cmdb_ci")).SendKeys("My Configuration Item" + Keys.Enter);
                driver.FindElement(By.Id("sys_display.change_request.assignment_group")).SendKeys("My Assignment Group" + Keys.Enter);
                driver.FindElement(By.Id("sys_display.change_request.assigned_to")).SendKeys("System Administrator" + Keys.Enter);

                // Fill in the Planning tab boxes at the bottom
                Console.WriteLine("Filling Planning tab documentation fields...");
                driver.FindElement(By.Id("change_request.justification")).SendKeys("Automated justification text.");
                driver.FindElement(By.Id("change_request.implementation_plan")).SendKeys("Automated implementation plan steps.");
                driver.FindElement(By.Id("change_request.risk_plan")).SendKeys("Automated risk and impact analysis.");
                driver.FindElement(By.Id("change_request.backout_plan")).SendKeys("Automated backout strategy plan.");

                Console.WriteLine("Workflow Completed: All requested form information has been typed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Automation Flow Interrupted: " + ex.Message);
            }
            finally
            {
                // Close browser instance cleanly
                driver.Quit();
            }
        }
    }

}
